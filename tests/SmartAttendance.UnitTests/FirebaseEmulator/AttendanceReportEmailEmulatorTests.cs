using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Google.Cloud.Firestore;
using Microsoft.Extensions.DependencyInjection;
using SmartAttendance.Infrastructure.Reporting.Email;

namespace SmartAttendance.UnitTests.FirebaseEmulator;

[Collection(FirebaseEmulatorTestGroup.Name)]
public sealed class AttendanceReportEmailEmulatorTests
    : IClassFixture<FirebaseEmulatorWebApplicationFactory>
{
    private readonly FirebaseEmulatorWebApplicationFactory _factory;

    public AttendanceReportEmailEmulatorTests(
        FirebaseEmulatorWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [FirebaseEmulatorTheory]
    [InlineData("admin")]
    [InlineData("manager")]
    public async Task ActiveAdminAndManagerGeneratePdfAndSendThroughFake(string role)
    {
        var caller = await CreateCallerAsync(role, "active");
        var employeeUid = await CreateEmployeeAsync();
        await WriteAttendanceAsync(employeeUid, "nfc_mobile");
        using var client = _factory.CreateClient();
        var before = Sender.Messages.Count;

        using var response = await SendAsync(
            client,
            caller.IdToken,
            employeeUid,
            Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var message = Assert.Single(Sender.Messages.Skip(before));
        Assert.Equal($"{employeeUid}@example.test", message.Recipient);
        Assert.True(message.AttachmentBytes.AsSpan().StartsWith("%PDF"u8));
        Assert.InRange(message.AttachmentBytes.Length, 1, 5 * 1024 * 1024);
    }

    [FirebaseEmulatorFact]
    public async Task EmployeeCannotCallReportEmailApi()
    {
        var caller = await CreateCallerAsync("employee", "active");
        var target = await CreateEmployeeAsync();
        using var client = _factory.CreateClient();

        using var response = await SendAsync(
            client,
            caller.IdToken,
            target,
            Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [FirebaseEmulatorFact]
    public async Task LockedCallerAndLockedTargetAreRejected()
    {
        var lockedCaller = await CreateCallerAsync("manager", "locked");
        var activeCaller = await CreateCallerAsync("admin", "active");
        var target = await CreateEmployeeAsync("locked");
        using var client = _factory.CreateClient();

        using var callerResponse = await SendAsync(
            client,
            lockedCaller.IdToken,
            target,
            Guid.NewGuid());
        using var targetResponse = await SendAsync(
            client,
            activeCaller.IdToken,
            target,
            Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, callerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, targetResponse.StatusCode);
    }

    [FirebaseEmulatorFact]
    public async Task ConcurrentSameIdempotencyKeySendsAtMostOnce()
    {
        var caller = await CreateCallerAsync("admin", "active");
        var target = await CreateEmployeeAsync();
        await WriteAttendanceAsync(target, "biometric");
        using var client = _factory.CreateClient();
        var before = Sender.Messages.Count;
        var key = Guid.NewGuid();

        var responses = await Task.WhenAll(
            SendAsync(client, caller.IdToken, target, key),
            SendAsync(client, caller.IdToken, target, key));
        using (responses[0])
        using (responses[1])
        {
            Assert.All(
                responses,
                response => Assert.Contains(
                    response.StatusCode,
                    new[] { HttpStatusCode.Accepted, HttpStatusCode.Conflict }));
        }
        Assert.Equal(1, Sender.Messages.Count - before);
    }

    [FirebaseEmulatorFact]
    public async Task SameIdempotencyKeyWithDifferentPayloadIsConflict()
    {
        var caller = await CreateCallerAsync("admin", "active");
        var firstTarget = await CreateEmployeeAsync();
        var secondTarget = await CreateEmployeeAsync();
        await WriteAttendanceAsync(firstTarget, "nfc_mobile");
        await WriteAttendanceAsync(secondTarget, "nfc_mobile");
        using var client = _factory.CreateClient();
        var key = Guid.NewGuid();

        using var first = await SendAsync(
            client,
            caller.IdToken,
            firstTarget,
            key);
        using var second = await SendAsync(
            client,
            caller.IdToken,
            secondTarget,
            key);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [FirebaseEmulatorFact]
    public async Task DifferentKeyForSameEmployeePeriodIsRateLimited()
    {
        var caller = await CreateCallerAsync("manager", "active");
        var target = await CreateEmployeeAsync();
        await WriteAttendanceAsync(target, "manual");
        using var client = _factory.CreateClient();

        using var first = await SendAsync(
            client,
            caller.IdToken,
            target,
            Guid.NewGuid());
        using var second = await SendAsync(
            client,
            caller.IdToken,
            target,
            Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [FirebaseEmulatorFact]
    public async Task CallerRollingMinuteLimitIsDurable()
    {
        var caller = await CreateCallerAsync("admin", "active");
        using var client = _factory.CreateClient();
        var responses = new List<HttpResponseMessage>();
        try
        {
            for (var index = 0; index < 6; index++)
            {
                var target = await CreateEmployeeAsync();
                await WriteAttendanceAsync(target, "nfc");
                responses.Add(await SendAsync(
                    client,
                    caller.IdToken,
                    target,
                    Guid.NewGuid()));
            }

            Assert.All(responses.Take(5), response =>
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
            Assert.Equal(HttpStatusCode.TooManyRequests, responses[5].StatusCode);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [FirebaseEmulatorFact]
    public async Task RecipientRollingHourLimitIsDurable()
    {
        var sharedEmail = $"shared-{Guid.NewGuid():N}@example.test";
        using var client = _factory.CreateClient();
        var responses = new List<HttpResponseMessage>();
        try
        {
            for (var index = 0; index < 4; index++)
            {
                var caller = await CreateCallerAsync("manager", "active");
                var target = await CreateEmployeeAsync(email: sharedEmail);
                await WriteAttendanceAsync(target, "nfc_mobile");
                responses.Add(await SendAsync(
                    client,
                    caller.IdToken,
                    target,
                    Guid.NewGuid()));
            }

            Assert.All(responses.Take(3), response =>
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
            Assert.Equal(HttpStatusCode.TooManyRequests, responses[3].StatusCode);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    private DevelopmentFakeReportEmailSender Sender =>
        _factory.Services.GetRequiredService<DevelopmentFakeReportEmailSender>();

    private async Task<EmulatorAccount> CreateCallerAsync(string role, string status)
    {
        var host = FirebaseEmulatorTestEnvironment.GetRequiredLoopbackHost(
            "FIREBASE_AUTH_EMULATOR_HOST");
        using var authClient = new HttpClient { BaseAddress = new Uri($"http://{host}") };
        var email = $"f04-{Guid.NewGuid():N}@example.test";
        using var response = await authClient.PostAsJsonAsync(
            "/identitytoolkit.googleapis.com/v1/accounts:signUp?key=demo-api-key",
            new { email, password = "EmulatorPass123!", returnSecureToken = true });
        response.EnsureSuccessStatusCode();
        var account = await response.Content.ReadFromJsonAsync<EmulatorAccount>()
            ?? throw new InvalidOperationException("Auth Emulator returned no account.");
        await Firestore.Collection("users").Document(account.Uid).SetAsync(
            new Dictionary<string, object>
            {
                ["fullName"] = $"F04 {role}",
                ["email"] = email,
                ["role"] = role,
                ["status"] = status,
            });
        return account;
    }

    private async Task<string> CreateEmployeeAsync(
        string status = "active",
        string? email = null)
    {
        var uid = $"employee-{Guid.NewGuid():N}";
        await Firestore.Collection("users").Document(uid).SetAsync(
            new Dictionary<string, object>
            {
                ["fullName"] = "Nhân viên F04",
                ["email"] = email ?? $"{uid}@example.test",
                ["role"] = "employee",
                ["status"] = status,
                ["hourlyRate"] = 50_000L,
            });
        return uid;
    }

    private Task<WriteResult> WriteAttendanceAsync(string employeeUid, string method) =>
        Firestore.Collection("attendance").Document(Guid.NewGuid().ToString("N"))
            .SetAsync(new Dictionary<string, object>
            {
                ["employeeId"] = employeeUid,
                ["workDate"] = "2026-10-05",
                ["status"] = "completed",
                ["method"] = method,
                ["checkInAt"] = Timestamp.FromDateTime(
                    new DateTime(2026, 10, 5, 1, 0, 0, DateTimeKind.Utc)),
                ["checkOutAt"] = Timestamp.FromDateTime(
                    new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc)),
            });

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string token,
        string employeeUid,
        Guid key)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/employees/{employeeUid}/attendance-reports/email")
        {
            Content = JsonContent.Create(new
            {
                periodKind = "month",
                anchorDate = "2026-10-05",
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", key.ToString("D"));
        return client.SendAsync(request);
    }

    private FirestoreDb Firestore =>
        _factory.Services.GetRequiredService<FirestoreDb>();

    private sealed record EmulatorAccount(
        [property: JsonPropertyName("localId")] string Uid,
        [property: JsonPropertyName("idToken")] string IdToken);
}

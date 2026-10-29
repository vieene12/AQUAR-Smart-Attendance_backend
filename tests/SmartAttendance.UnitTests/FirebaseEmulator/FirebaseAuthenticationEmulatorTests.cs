using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FirebaseAdmin.Auth;
using Google.Cloud.Firestore;
using Microsoft.Extensions.DependencyInjection;
using SmartAttendance.Api.Authentication;

namespace SmartAttendance.UnitTests.FirebaseEmulator;

[Collection(FirebaseEmulatorTestGroup.Name)]
public sealed class FirebaseAuthenticationEmulatorTests
    : IClassFixture<FirebaseEmulatorWebApplicationFactory>
{
    private readonly FirebaseEmulatorWebApplicationFactory _factory;

    public FirebaseAuthenticationEmulatorTests(
        FirebaseEmulatorWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [FirebaseEmulatorFact]
    public async Task AuthTokenAndFirestoreProfileAreAcceptedEndToEnd()
    {
        var account = await CreateAccountAsync();
        await WriteProfileAsync(account.Uid, "employee", "active");
        using var client = _factory.CreateClient();

        var response = await SendWithTokenAsync(
            client,
            "/api/auth/me",
            account.IdToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var caller = await response.Content
            .ReadFromJsonAsync<AuthenticatedCallerResponse>();
        Assert.NotNull(caller);
        Assert.Equal(account.Uid, caller.Uid);
        Assert.Equal("employee", caller.Role);
        Assert.Equal("active", caller.Status);
    }

    [FirebaseEmulatorFact]
    public async Task ActiveAdminCanAccessAdminEndpointEndToEnd()
    {
        var account = await CreateAccountAsync();
        await WriteProfileAsync(account.Uid, "admin", "active");
        using var client = _factory.CreateClient();

        var response = await SendWithTokenAsync(
            client,
            "/api/auth/admin-check",
            account.IdToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [FirebaseEmulatorTheory]
    [InlineData("manager")]
    [InlineData("employee")]
    public async Task NonAdminCannotAccessAdminEndpointEndToEnd(string role)
    {
        var account = await CreateAccountAsync();
        await WriteProfileAsync(account.Uid, role, "active");
        using var client = _factory.CreateClient();

        var response = await SendWithTokenAsync(
            client,
            "/api/auth/admin-check",
            account.IdToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [FirebaseEmulatorFact]
    public async Task LockedUserIsRejectedEndToEnd()
    {
        var account = await CreateAccountAsync();
        await WriteProfileAsync(account.Uid, "admin", "locked");
        using var client = _factory.CreateClient();

        var response = await SendWithTokenAsync(
            client,
            "/api/auth/admin-check",
            account.IdToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [FirebaseEmulatorFact]
    public async Task MissingProfileIsRejectedEndToEnd()
    {
        var account = await CreateAccountAsync();
        using var client = _factory.CreateClient();

        var response = await SendWithTokenAsync(
            client,
            "/api/auth/me",
            account.IdToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [FirebaseEmulatorFact]
    public async Task InvalidTokenIsRejectedEndToEnd()
    {
        using var client = _factory.CreateClient();

        var response = await SendWithTokenAsync(
            client,
            "/api/auth/me",
            "not-a-firebase-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [FirebaseEmulatorKnownLimitationFact]
    public async Task DisabledAuthAccountIsRejectedWhenRevocationCheckIsEnabled()
    {
        var account = await CreateAccountAsync();
        await WriteProfileAsync(account.Uid, "admin", "active");
        var firebaseAuth = _factory.Services.GetRequiredService<FirebaseAuth>();
        await firebaseAuth.UpdateUserAsync(new UserRecordArgs
        {
            Uid = account.Uid,
            Disabled = true,
        });
        using var client = _factory.CreateClient();

        var response = await SendWithTokenAsync(
            client,
            "/api/auth/admin-check",
            account.IdToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [FirebaseEmulatorFact]
    public async Task RevokedTokenIsRejectedWhenRevocationCheckIsEnabled()
    {
        var account = await CreateAccountAsync();
        await WriteProfileAsync(account.Uid, "admin", "active");
        var firebaseAuth = _factory.Services.GetRequiredService<FirebaseAuth>();
        await Task.Delay(TimeSpan.FromSeconds(1.1));
        await firebaseAuth.RevokeRefreshTokensAsync(account.Uid);
        using var client = _factory.CreateClient();

        var response = await SendWithTokenAsync(
            client,
            "/api/auth/admin-check",
            account.IdToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [FirebaseEmulatorFact]
    public async Task AdminCreatesEmployeeInAuthAndFirestoreEndToEnd()
    {
        var admin = await CreateAccountAsync();
        await WriteProfileAsync(admin.Uid, "admin", "active");
        using var client = _factory.CreateClient();
        var email = $"created-{Guid.NewGuid():N}@example.test";

        using var response = await SendJsonWithTokenAsync(
            client,
            HttpMethod.Post,
            "/api/accounts",
            admin.IdToken,
            new
            {
                fullName = "  Nguyễn Văn E2E  ",
                email,
                password = "EmulatorPass123!",
                role = "employee",
                hourlyRate = 50_000,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<AccountApiResponse>();
        Assert.NotNull(created);
        Assert.Equal("Nguyễn Văn E2E", created.FullName);
        Assert.Equal("active", created.Status);
        Assert.DoesNotContain("password", await response.Content.ReadAsStringAsync());
        var authUser = await _factory.Services.GetRequiredService<FirebaseAuth>()
            .GetUserAsync(created.Uid);
        Assert.Equal(email, authUser.Email);
        Assert.Equal(created.FullName, authUser.DisplayName);
        Assert.False(authUser.Disabled);
        var profile = await GetProfileAsync(created.Uid);
        Assert.Equal(email, profile.GetValue<string>("email"));
        Assert.Equal(50_000, profile.GetValue<long>("hourlyRate"));
    }

    [FirebaseEmulatorFact]
    public async Task AdminListsAccountsWithBoundedPaginationEndToEnd()
    {
        var admin = await CreateAccountAsync();
        await WriteProfileAsync(admin.Uid, "admin", "active");
        using var client = _factory.CreateClient();
        var employee = await CreateThroughApiAsync(client, admin.IdToken, "employee");
        var manager = await CreateThroughApiAsync(client, admin.IdToken, "manager");
        var allItems = new List<AccountApiResponse>();
        string? pageToken = null;

        do
        {
            var uri = "/api/accounts?pageSize=2";
            if (pageToken is not null)
            {
                uri += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            using var response = await SendWithTokenAsync(client, uri, admin.IdToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = await response.Content.ReadFromJsonAsync<AccountPageApiResponse>();
            Assert.NotNull(page);
            Assert.InRange(page.Items.Count, 1, 2);
            allItems.AddRange(page.Items);
            pageToken = page.NextPageToken;
        }
        while (pageToken is not null);

        Assert.Equal(allItems.Count, allItems.Select(item => item.Uid).Distinct().Count());
        Assert.Contains(allItems, item => item.Uid == admin.Uid && item.Role == "admin");
        Assert.Contains(allItems, item => item.Uid == employee.Uid);
        Assert.Contains(allItems, item => item.Uid == manager.Uid);
        Assert.All(
            allItems,
            item => Assert.True(item.Role is "admin" or "employee" or "manager"));
    }

    [FirebaseEmulatorFact]
    public async Task AdminUpdatesAllowedFieldsInAuthAndFirestoreEndToEnd()
    {
        var admin = await CreateAccountAsync();
        await WriteProfileAsync(admin.Uid, "admin", "active");
        using var client = _factory.CreateClient();
        var created = await CreateThroughApiAsync(client, admin.IdToken, "employee");

        using var response = await SendJsonWithTokenAsync(
            client,
            HttpMethod.Put,
            $"/api/accounts/{created.Uid}",
            admin.IdToken,
            new { fullName = "Tên Đã Sửa", hourlyRate = 75_000 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var authUser = await _factory.Services.GetRequiredService<FirebaseAuth>()
            .GetUserAsync(created.Uid);
        Assert.Equal("Tên Đã Sửa", authUser.DisplayName);
        var profile = await GetProfileAsync(created.Uid);
        Assert.Equal("Tên Đã Sửa", profile.GetValue<string>("fullName"));
        Assert.Equal(75_000, profile.GetValue<long>("hourlyRate"));
        Assert.Equal("employee", profile.GetValue<string>("role"));
        Assert.Equal(created.Email, profile.GetValue<string>("email"));
    }

    [FirebaseEmulatorFact]
    public async Task AdminLocksAndUnlocksAuthAndFirestoreEndToEnd()
    {
        var admin = await CreateAccountAsync();
        await WriteProfileAsync(admin.Uid, "admin", "active");
        using var client = _factory.CreateClient();
        var created = await CreateThroughApiAsync(client, admin.IdToken, "employee");

        using var lockResponse = await SendJsonWithTokenAsync(
            client,
            HttpMethod.Patch,
            $"/api/accounts/{created.Uid}/status",
            admin.IdToken,
            new { status = "locked" });
        Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);
        Assert.True((await GetAuthUserAsync(created.Uid)).Disabled);
        Assert.Equal(
            "locked",
            (await GetProfileAsync(created.Uid)).GetValue<string>("status"));

        using var unlockResponse = await SendJsonWithTokenAsync(
            client,
            HttpMethod.Patch,
            $"/api/accounts/{created.Uid}/status",
            admin.IdToken,
            new { status = "active" });
        Assert.Equal(HttpStatusCode.OK, unlockResponse.StatusCode);
        Assert.False((await GetAuthUserAsync(created.Uid)).Disabled);
        Assert.Equal(
            "active",
            (await GetProfileAsync(created.Uid)).GetValue<string>("status"));
    }

    [FirebaseEmulatorTheory]
    [InlineData("manager")]
    [InlineData("employee")]
    public async Task NonAdminCannotUseAccountsApiEndToEnd(string role)
    {
        var caller = await CreateAccountAsync();
        await WriteProfileAsync(caller.Uid, role, "active");
        using var client = _factory.CreateClient();

        using var response = await SendWithTokenAsync(
            client,
            "/api/accounts",
            caller.IdToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [FirebaseEmulatorFact]
    public async Task AdminAndInvalidDataAreProtectedEndToEnd()
    {
        var admin = await CreateAccountAsync();
        await WriteProfileAsync(admin.Uid, "admin", "active");
        var otherAdmin = await CreateAccountAsync();
        await WriteProfileAsync(otherAdmin.Uid, "admin", "active");
        using var client = _factory.CreateClient();

        using var selfLock = await SendJsonWithTokenAsync(
            client,
            HttpMethod.Patch,
            $"/api/accounts/{admin.Uid}/status",
            admin.IdToken,
            new { status = "locked" });
        Assert.Equal(HttpStatusCode.Forbidden, selfLock.StatusCode);

        using var otherAdminEdit = await SendJsonWithTokenAsync(
            client,
            HttpMethod.Put,
            $"/api/accounts/{otherAdmin.Uid}",
            admin.IdToken,
            new { fullName = "Không được sửa", hourlyRate = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, otherAdminEdit.StatusCode);

        using var otherAdminLock = await SendJsonWithTokenAsync(
            client,
            HttpMethod.Patch,
            $"/api/accounts/{otherAdmin.Uid}/status",
            admin.IdToken,
            new { status = "locked" });
        Assert.Equal(HttpStatusCode.Forbidden, otherAdminLock.StatusCode);

        using var invalidCreate = await SendJsonWithTokenAsync(
            client,
            HttpMethod.Post,
            "/api/accounts",
            admin.IdToken,
            new
            {
                fullName = "Invalid Admin",
                email = $"invalid-{Guid.NewGuid():N}@example.test",
                password = "EmulatorPass123!",
                role = "admin",
                hourlyRate = 1,
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCreate.StatusCode);
    }

    [FirebaseEmulatorFact]
    public async Task DuplicateEmailIsRejectedWithoutSecondProfileEndToEnd()
    {
        var admin = await CreateAccountAsync();
        await WriteProfileAsync(admin.Uid, "admin", "active");
        using var client = _factory.CreateClient();
        var email = $"duplicate-{Guid.NewGuid():N}@example.test";
        await CreateThroughApiAsync(client, admin.IdToken, "employee", email);

        using var duplicate = await SendJsonWithTokenAsync(
            client,
            HttpMethod.Post,
            "/api/accounts",
            admin.IdToken,
            CreatePayload("manager", email));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    private static async Task<EmulatorAccount> CreateAccountAsync()
    {
        var authHost = FirebaseEmulatorTestEnvironment.GetRequiredLoopbackHost(
            "FIREBASE_AUTH_EMULATOR_HOST");
        using var authClient = new HttpClient
        {
            BaseAddress = new Uri($"http://{authHost}"),
        };
        var email = $"backend-e2e-{Guid.NewGuid():N}@example.test";
        using var response = await authClient.PostAsJsonAsync(
            "/identitytoolkit.googleapis.com/v1/accounts:signUp?key=demo-api-key",
            new
            {
                email,
                password = "EmulatorPass123!",
                returnSecureToken = true,
            });
        response.EnsureSuccessStatusCode();
        var account = await response.Content.ReadFromJsonAsync<EmulatorAccount>();
        return account ?? throw new InvalidOperationException(
            "Auth Emulator returned an empty account response.");
    }

    private async Task WriteProfileAsync(string uid, string role, string status)
    {
        var firestore = _factory.Services.GetRequiredService<FirestoreDb>();
        var data = role == "admin"
            ? new Dictionary<string, object>
            {
                ["name"] = "E2E admin",
                ["role"] = role,
                ["status"] = status,
            }
            : new Dictionary<string, object>
            {
                ["fullName"] = $"E2E {role}",
                ["email"] = $"{uid}@example.test",
                ["role"] = role,
                ["status"] = status,
                ["hourlyRate"] = 0L,
            };
        await firestore.Collection("users").Document(uid).SetAsync(data);
    }

    private async Task<DocumentSnapshot> GetProfileAsync(string uid)
    {
        var firestore = _factory.Services.GetRequiredService<FirestoreDb>();
        return await firestore.Collection("users").Document(uid).GetSnapshotAsync();
    }

    private async Task<UserRecord> GetAuthUserAsync(string uid) =>
        await _factory.Services.GetRequiredService<FirebaseAuth>().GetUserAsync(uid);

    private static async Task<AccountApiResponse> CreateThroughApiAsync(
        HttpClient client,
        string token,
        string role,
        string? email = null)
    {
        using var response = await SendJsonWithTokenAsync(
            client,
            HttpMethod.Post,
            "/api/accounts",
            token,
            CreatePayload(role, email));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<AccountApiResponse>()
            ?? throw new InvalidOperationException("Accounts API returned no account.");
    }

    private static object CreatePayload(string role, string? email = null) => new
    {
        fullName = $"E2E {role}",
        email = email ?? $"f02-{Guid.NewGuid():N}@example.test",
        password = "EmulatorPass123!",
        role,
        hourlyRate = 50_000,
    };

    private static async Task<HttpResponseMessage> SendJsonWithTokenAsync<T>(
        HttpClient client,
        HttpMethod method,
        string uri,
        string token,
        T payload)
    {
        using var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendWithTokenAsync(
        HttpClient client,
        string requestUri,
        string idToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            idToken);
        return await client.SendAsync(request);
    }

    private sealed record EmulatorAccount(
        [property: JsonPropertyName("localId")] string Uid,
        [property: JsonPropertyName("idToken")] string IdToken);

    private sealed record AccountApiResponse(
        string Uid,
        string? FullName,
        string? Email,
        string Role,
        string Status,
        long? HourlyRate);

    private sealed record AccountPageApiResponse(
        IReadOnlyList<AccountApiResponse> Items,
        string? NextPageToken);
}

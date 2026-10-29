using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SmartAttendance.Application.Reports;

namespace SmartAttendance.UnitTests.Reports;

public sealed class AttendanceReportEmailEndpointTests
{
    [Fact]
    public async Task MissingTokenIsUnauthorized()
    {
        await using var factory = Factory("admin", "active", out _);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/employees/employee-uid/attendance-reports/email",
            new { periodKind = "month", anchorDate = "2026-10-05" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MissingIdempotencyKeyIsBadRequest()
    {
        await using var factory = Factory("admin", "active", out _);
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/employees/employee-uid/attendance-reports/email")
        {
            Content = JsonContent.Create(new
            {
                periodKind = "month",
                anchorDate = "2026-10-05",
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "valid-test-token");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("manager")]
    public async Task ActiveAdminAndManagerCanSend(string role)
    {
        await using var factory = Factory(role, "active", out var service);
        using var client = factory.CreateClient();

        var response = await SendAsync(client);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("caller-uid", service.Command?.CallerUid);
        Assert.Equal("employee-uid", service.Command?.EmployeeUid);
        Assert.Equal("month", service.Command?.PeriodKind);
    }

    [Fact]
    public async Task EmployeeIsForbidden()
    {
        await using var factory = Factory("employee", "active", out _);
        using var client = factory.CreateClient();

        var response = await SendAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LockedManagerIsUnauthorized()
    {
        await using var factory = Factory("manager", "locked", out _);
        using var client = factory.CreateClient();

        var response = await SendAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(FailureMappings))]
    public async Task MapsDomainFailure(Exception exception, HttpStatusCode expected)
    {
        await using var factory = Factory("admin", "active", out var service);
        service.Exception = exception;
        using var client = factory.CreateClient();

        var response = await SendAsync(client);

        Assert.Equal(expected, response.StatusCode);
    }

    public static TheoryData<Exception, HttpStatusCode> FailureMappings => new()
    {
        { new ReportValidationException("bad"), HttpStatusCode.BadRequest },
        { new ReportEmployeeNotFoundException("missing"), HttpStatusCode.NotFound },
        { new ReportRequestConflictException("duplicate"), HttpStatusCode.Conflict },
        { new ReportPayloadTooLargeException("large"), HttpStatusCode.RequestEntityTooLarge },
        { new ReportEmailInvalidException("email"), HttpStatusCode.UnprocessableEntity },
        { new ReportRateLimitException("rate"), HttpStatusCode.TooManyRequests },
        { new ReportEmailUnavailableException("provider"), HttpStatusCode.ServiceUnavailable },
    };

    private static ReportEmailApiWebApplicationFactory Factory(
        string role,
        string status,
        out StubReportEmailService service)
    {
        service = new StubReportEmailService();
        return new ReportEmailApiWebApplicationFactory(role, status, service);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/employees/employee-uid/attendance-reports/email")
        {
            Content = JsonContent.Create(new
            {
                periodKind = "month",
                anchorDate = "2026-10-05",
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "valid-test-token");
        request.Headers.Add(
            "Idempotency-Key",
            "11111111-1111-1111-1111-111111111111");
        return client.SendAsync(request);
    }
}

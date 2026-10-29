using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SmartAttendance.Domain.Accounts;

namespace SmartAttendance.UnitTests.Accounts;

public sealed class AccountsEndpointTests
{
    [Fact]
    public async Task AccountsRequireAuthentication()
    {
        await using var factory = CreateFactory("admin", out _);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/accounts",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("employee")]
    public async Task NonAdminCannotCallAccounts(string role)
    {
        await using var factory = CreateFactory(role, out _);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, HttpMethod.Get, "/api/accounts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ActiveAdminCanListWithBoundedPagination()
    {
        await using var factory = CreateFactory("admin", out var store);
        store.Page = new(
            [new ManagedAccount("uid", "Name", "name@example.com", "employee", "active", 1)],
            "next-token");
        using var client = factory.CreateClient();

        var response = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/accounts?pageSize=25&pageToken=current-token");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("list:25:current-token", store.Calls);
    }

    [Fact]
    public async Task CreateDoesNotReturnPasswordAndRejectsAdminRole()
    {
        await using var factory = CreateFactory("admin", out _);
        using var client = factory.CreateClient();
        var payload = new
        {
            fullName = "Admin Two",
            email = "admin2@example.com",
            password = "secret123",
            role = "admin",
            hourlyRate = 1,
        };

        var response = await SendJsonAsync(client, HttpMethod.Post, "/api/accounts", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("secret123", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UpdateRejectsRoleOrStatusFieldsOutsideContract()
    {
        await using var factory = CreateFactory("admin", out _);
        using var client = factory.CreateClient();
        var payload = new
        {
            fullName = "Changed",
            hourlyRate = 1,
            role = "admin",
        };

        var response = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/accounts/employee-uid",
            payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CallingAdminCannotLockSelf()
    {
        await using var factory = CreateFactory("admin", out _);
        using var client = factory.CreateClient();

        var response = await SendJsonAsync(
            client,
            HttpMethod.Patch,
            $"/api/accounts/{AccountApiWebApplicationFactory.CallerUid}/status",
            new { status = "locked" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static AccountApiWebApplicationFactory CreateFactory(
        string role,
        out FakeAccountStore store)
    {
        store = new FakeAccountStore();
        return new AccountApiWebApplicationFactory(role, store);
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string uri) =>
        SendAsync(client, new HttpRequestMessage(method, uri));

    private static Task<HttpResponseMessage> SendJsonAsync<T>(
        HttpClient client,
        HttpMethod method,
        string uri,
        T payload)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(payload),
        };
        return SendAsync(client, request);
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "valid-test-token");
        return client.SendAsync(request, CancellationToken.None);
    }
}

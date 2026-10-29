using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using SmartAttendance.Api.Authentication;
using SmartAttendance.Application.Authentication;

namespace SmartAttendance.UnitTests.Authentication;

public sealed class FirebaseAuthenticationEndpointTests
{
    private const string Uid = "test-user-uid";

    [Fact]
    public async Task DiagnosticEndpointsAreNotMappedInProduction()
    {
        await using var testFactory = CreateFactory(ActiveProfile("admin"));
        await using var productionFactory = testFactory.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production"));
        using var client = productionFactory.CreateClient();
        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdminCheckWithoutTokenReturnsUnauthorized()
    {
        await using var factory = CreateFactory(ActiveProfile("admin"));
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/auth/admin-check");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminCheckWithInvalidTokenReturnsUnauthorized()
    {
        var verifier = new FakeFirebaseIdTokenVerifier((_, _) =>
            throw new InvalidFirebaseIdTokenException("Invalid token."));
        await using var factory = new FirebaseSecurityWebApplicationFactory(
            verifier,
            new FakeUserProfileReader(ActiveProfile("admin")));
        using var client = factory.CreateClient();
        var response = await SendWithTokenAsync(client, "/api/auth/admin-check");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ActiveAdminCanAccessAdminCheck()
    {
        await using var factory = CreateFactory(ActiveProfile("admin"));
        using var client = factory.CreateClient();
        var response = await SendWithTokenAsync(client, "/api/auth/admin-check");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var caller = await response.Content.ReadFromJsonAsync<AuthenticatedCallerResponse>();
        Assert.NotNull(caller);
        Assert.Equal(Uid, caller.Uid);
        Assert.Equal("admin", caller.Role);
        Assert.Equal("active", caller.Status);
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("employee")]
    public async Task NonAdminCannotAccessAdminCheck(string role)
    {
        await using var factory = CreateFactory(ActiveProfile(role));
        using var client = factory.CreateClient();
        var response = await SendWithTokenAsync(client, "/api/auth/admin-check");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LockedAdminCannotAccessAdminCheck()
    {
        await using var factory = CreateFactory(new UserProfile(Uid, "admin", "locked"));
        using var client = factory.CreateClient();
        var response = await SendWithTokenAsync(client, "/api/auth/admin-check");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MissingProfileReturnsUnauthorized()
    {
        await using var factory = CreateFactory(profile: null);
        using var client = factory.CreateClient();
        var response = await SendWithTokenAsync(client, "/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("owner", "active")]
    [InlineData("admin", "disabled")]
    public async Task InvalidProfileReturnsUnauthorized(string role, string status)
    {
        await using var factory = CreateFactory(new UserProfile(Uid, role, status));
        using var client = factory.CreateClient();
        var response = await SendWithTokenAsync(client, "/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FirebaseUnavailableReturnsServiceUnavailableAndNeverAllowsAccess()
    {
        var verifier = new FakeFirebaseIdTokenVerifier((_, _) =>
            throw new FirebaseServiceUnavailableException("Unavailable."));
        await using var factory = new FirebaseSecurityWebApplicationFactory(
            verifier,
            new FakeUserProfileReader(ActiveProfile("admin")));
        using var client = factory.CreateClient();
        var response = await SendWithTokenAsync(client, "/api/auth/admin-check");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task FirestoreUnavailableReturnsServiceUnavailableAndNeverAllowsAccess()
    {
        var verifier = new FakeFirebaseIdTokenVerifier((_, _) =>
            new FirebaseIdentity(Uid));
        var profileReader = new FakeUserProfileReader(_ =>
            throw new FirebaseServiceUnavailableException("Unavailable."));
        await using var factory = new FirebaseSecurityWebApplicationFactory(
            verifier,
            profileReader);
        using var client = factory.CreateClient();
        var response = await SendWithTokenAsync(client, "/api/auth/admin-check");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticationAlwaysRequestsRevocationCheck()
    {
        var checkRevokedObserved = false;
        var verifier = new FakeFirebaseIdTokenVerifier((_, checkRevoked) =>
        {
            checkRevokedObserved = checkRevoked;
            return new FirebaseIdentity(Uid);
        });
        await using var factory = new FirebaseSecurityWebApplicationFactory(
            verifier,
            new FakeUserProfileReader(ActiveProfile("admin")));
        using var client = factory.CreateClient();
        var response = await SendWithTokenAsync(client, "/api/auth/admin-check");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(checkRevokedObserved);
    }

    private static FirebaseSecurityWebApplicationFactory CreateFactory(UserProfile? profile)
    {
        var verifier = new FakeFirebaseIdTokenVerifier((token, _) =>
            token == "valid-test-token"
                ? new FirebaseIdentity(Uid)
                : throw new InvalidFirebaseIdTokenException("Invalid token."));
        return new FirebaseSecurityWebApplicationFactory(
            verifier,
            new FakeUserProfileReader(profile));
    }

    private static UserProfile ActiveProfile(string role) => new(Uid, role, "active");

    private static Task<HttpResponseMessage> SendWithTokenAsync(
        HttpClient client,
        string requestUri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "valid-test-token");
        return client.SendAsync(request);
    }
}

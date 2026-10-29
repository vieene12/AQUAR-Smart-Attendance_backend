using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartAttendance.Api.Health;

namespace SmartAttendance.UnitTests.Health;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealthReturnsPublicHealthyJson()
    {
        var response = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(payload);
        Assert.Equal("healthy", payload.Status);
        Assert.Equal("smart-attendance-api", payload.Service);
        Assert.Equal("1.0.0", payload.Version);
        Assert.True(payload.TimestampUtc <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ProductionRootRedirectsToSwagger()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/swagger", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ProductionSwaggerExposesApiDocumentAndBearerScheme()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = factory.CreateClient();

        var uiResponse = await client.GetAsync("/swagger/index.html");
        var documentResponse = await client.GetAsync("/swagger/v1/swagger.json");
        var document = await documentResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, uiResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, documentResponse.StatusCode);
        Assert.Contains("\"/api/health\"", document, StringComparison.Ordinal);
        Assert.Contains("\"/api/accounts\"", document, StringComparison.Ordinal);
        Assert.Contains("\"bearer\"", document, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private_key", document, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("resend", document, StringComparison.OrdinalIgnoreCase);
    }
}

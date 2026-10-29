using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Api.Controllers;
using SmartAttendance.Api.Health;

namespace SmartAttendance.UnitTests.Health;

public sealed class HealthControllerTests
{
    [Fact]
    public void GetReturnsHealthyResponseWithoutSensitiveConfiguration()
    {
        var before = DateTimeOffset.UtcNow;
        var result = new HealthController().Get();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<HealthResponse>(ok.Value);
        Assert.Equal("healthy", response.Status);
        Assert.Equal("smart-attendance-api", response.Service);
        Assert.Equal("1.0.0", response.Version);
        Assert.InRange(response.TimestampUtc, before, DateTimeOffset.UtcNow);
    }
}

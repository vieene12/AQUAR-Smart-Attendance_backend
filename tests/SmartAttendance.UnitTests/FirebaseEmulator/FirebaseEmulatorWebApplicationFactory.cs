using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SmartAttendance.UnitTests.FirebaseEmulator;

public sealed class FirebaseEmulatorWebApplicationFactory
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var configuredProjectId = Environment.GetEnvironmentVariable(
            "Firebase__ProjectId");
        if (!string.Equals(
                configuredProjectId,
                FirebaseEmulatorTestEnvironment.ProjectId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Firebase__ProjectId must be {FirebaseEmulatorTestEnvironment.ProjectId}.");
        }

        var authHost = FirebaseEmulatorTestEnvironment.GetRequiredLoopbackHost(
            "FIREBASE_AUTH_EMULATOR_HOST");
        var firestoreHost = FirebaseEmulatorTestEnvironment.GetRequiredLoopbackHost(
            "FIRESTORE_EMULATOR_HOST");

        builder.UseEnvironment("Development");
        builder.UseSetting(
            "Firebase:ProjectId",
            FirebaseEmulatorTestEnvironment.ProjectId);
        builder.UseSetting("FIREBASE_AUTH_EMULATOR_HOST", authHost);
        builder.UseSetting("FIRESTORE_EMULATOR_HOST", firestoreHost);
        builder.UseSetting("Email:Mode", "Fake");
        builder.UseSetting("Email:Enabled", "true");
        builder.UseSetting("Email:ApiKey", string.Empty);
        builder.UseSetting("GOOGLE_APPLICATION_CREDENTIALS", string.Empty);
    }
}

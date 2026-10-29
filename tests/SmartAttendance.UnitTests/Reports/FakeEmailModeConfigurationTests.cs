using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SmartAttendance.Application.Reports;
using SmartAttendance.Infrastructure.Reporting;
using SmartAttendance.Infrastructure.Reporting.Email;

namespace SmartAttendance.UnitTests.Reports;

public sealed class FakeEmailModeConfigurationTests
{
    [Fact]
    public void ValidDevelopmentEmulatorConfigurationRegistersOnlyFakeSender()
    {
        var services = new ServiceCollection();

        services.AddReportEmailInfrastructure(
            Configuration(),
            Environment("Development"));

        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IReportEmailSender>();
        Assert.IsType<DevelopmentFakeReportEmailSender>(sender);
    }

    [Theory]
    [InlineData("Production", "demo-smart-attendance", "127.0.0.1:9099", "127.0.0.1:8081", "", "")]
    [InlineData("Staging", "demo-smart-attendance", "127.0.0.1:9099", "127.0.0.1:8081", "", "")]
    [InlineData("Development", "smart-attendance-32d6b", "127.0.0.1:9099", "127.0.0.1:8081", "", "")]
    [InlineData("Development", "demo-smart-attendance", "production.example:9099", "127.0.0.1:8081", "", "")]
    [InlineData("Development", "demo-smart-attendance", "127.0.0.1:9099", "production.example:8081", "", "")]
    [InlineData("Development", "demo-smart-attendance", "127.0.0.1:9099", "127.0.0.1:8081", "credential.json", "")]
    [InlineData("Development", "demo-smart-attendance", "127.0.0.1:9099", "127.0.0.1:8081", "", "resend-key")]
    public void UnsafeFakeConfigurationFailsFast(
        string environment,
        string projectId,
        string authHost,
        string firestoreHost,
        string credentials,
        string apiKey)
    {
        var configuration = Configuration(
            projectId,
            authHost,
            firestoreHost,
            credentials,
            apiKey);

        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddReportEmailInfrastructure(
                configuration,
                Environment(environment)));
    }

    [Fact]
    public void FakeModeRequiresExplicitEnabledFlag()
    {
        var values = SafeValues();
        values["Email:Enabled"] = "false";

        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddReportEmailInfrastructure(
                Build(values),
                Environment("Development")));
    }

    [Fact]
    public void DefaultDisabledResendModeRemainsAvailable()
    {
        var values = new Dictionary<string, string?>
        {
            ["Email:Enabled"] = "false",
            ["Email:Mode"] = "Resend",
        };
        var services = new ServiceCollection();

        services.AddReportEmailInfrastructure(
            Build(values),
            Environment("Production"));

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType ==
                typeof(DevelopmentFakeReportEmailSender));
    }

    [Fact]
    public async Task FakeSenderCapturesIndependentPdfCopyWithoutHttp()
    {
        var sender = new DevelopmentFakeReportEmailSender();
        var bytes = "%PDF-fake"u8.ToArray();
        var message = new ReportEmailMessage(
            "request-id",
            "employee@example.test",
            "Employee",
            new ReportPeriod(
                ReportPeriodKind.Month,
                new DateOnly(2026, 10, 1),
                new DateOnly(2026, 10, 31)),
            "Attendance report",
            "Body",
            "report.pdf",
            bytes);

        var result = await sender.SendAsync(message, CancellationToken.None);
        bytes[0] = 0;

        Assert.Equal("fake-request-id", result.ProviderMessageId);
        var captured = Assert.Single(sender.Messages);
        Assert.True(captured.AttachmentBytes.AsSpan().StartsWith("%PDF"u8));
        Assert.Equal("employee@example.test", captured.Recipient);
        Assert.Equal("report.pdf", captured.AttachmentFileName);
    }

    private static IConfiguration Configuration(
        string projectId = "demo-smart-attendance",
        string authHost = "127.0.0.1:9099",
        string firestoreHost = "127.0.0.1:8081",
        string credentials = "",
        string apiKey = "")
    {
        var values = SafeValues();
        values["Firebase:ProjectId"] = projectId;
        values["FIREBASE_AUTH_EMULATOR_HOST"] = authHost;
        values["FIRESTORE_EMULATOR_HOST"] = firestoreHost;
        values["GOOGLE_APPLICATION_CREDENTIALS"] = credentials;
        values["Email:ApiKey"] = apiKey;
        return Build(values);
    }

    private static Dictionary<string, string?> SafeValues() => new()
    {
        ["Firebase:ProjectId"] = "demo-smart-attendance",
        ["FIREBASE_AUTH_EMULATOR_HOST"] = "127.0.0.1:9099",
        ["FIRESTORE_EMULATOR_HOST"] = "127.0.0.1:8081",
        ["GOOGLE_APPLICATION_CREDENTIALS"] = "",
        ["Email:Enabled"] = "true",
        ["Email:Mode"] = "Fake",
        ["Email:ApiKey"] = "",
    };

    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static TestHostEnvironment Environment(string name) =>
        new TestHostEnvironment { EnvironmentName = name };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = "SmartAttendance.UnitTests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}

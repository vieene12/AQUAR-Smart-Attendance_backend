using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuestPDF.Infrastructure;
using SmartAttendance.Application.Reports;
using SmartAttendance.Infrastructure.Reporting.Configuration;
using SmartAttendance.Infrastructure.Reporting.Email;
using SmartAttendance.Infrastructure.Reporting.Pdf;

namespace SmartAttendance.Infrastructure.Reporting;

public static class DependencyInjection
{
    public static IServiceCollection AddReportEmailInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        services.Configure<EmailOptions>(
            configuration.GetSection(EmailOptions.SectionName));
        services.AddSingleton<IAttendanceReportPdfGenerator, QuestAttendanceReportPdfGenerator>();

        var email = configuration
            .GetSection(EmailOptions.SectionName)
            .Get<EmailOptions>() ?? new EmailOptions();
        var mode = string.IsNullOrWhiteSpace(email.Mode)
            ? EmailOptions.ResendMode
            : email.Mode.Trim();

        if (string.Equals(mode, EmailOptions.FakeMode, StringComparison.OrdinalIgnoreCase))
        {
            ValidateFakeMode(configuration, environment, email);
            services.AddSingleton<DevelopmentFakeReportEmailSender>();
            services.AddSingleton<IReportEmailSender>(provider =>
                provider.GetRequiredService<DevelopmentFakeReportEmailSender>());
            return services;
        }

        if (!string.Equals(mode, EmailOptions.ResendMode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Email:Mode must be Resend or Fake.");
        }

        services.AddHttpClient<IReportEmailSender, ResendEmailSender>((provider, client) =>
        {
            var options = provider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailOptions>>()
                .Value;
            client.BaseAddress = new Uri("https://api.resend.com/");
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 60));
        });
        return services;
    }

    private static void ValidateFakeMode(
        IConfiguration configuration,
        IHostEnvironment environment,
        EmailOptions email)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Email Fake mode is allowed only in Development.");
        }
        if (!email.Enabled)
        {
            throw new InvalidOperationException(
                "Email Fake mode requires Email:Enabled=true.");
        }
        if (!string.Equals(
                configuration["Firebase:ProjectId"],
                "demo-smart-attendance",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Email Fake mode requires Firebase project demo-smart-attendance.");
        }
        if (!string.Equals(
                configuration["FIREBASE_AUTH_EMULATOR_HOST"],
                "127.0.0.1:9099",
                StringComparison.Ordinal) ||
            !string.Equals(
                configuration["FIRESTORE_EMULATOR_HOST"],
                "127.0.0.1:8081",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Email Fake mode requires the loopback Auth and Firestore Emulators.");
        }
        if (!string.IsNullOrWhiteSpace(
                configuration["GOOGLE_APPLICATION_CREDENTIALS"]) ||
            !string.IsNullOrWhiteSpace(email.ApiKey))
        {
            throw new InvalidOperationException(
                "Email Fake mode forbids production credentials and provider API keys.");
        }
    }
}

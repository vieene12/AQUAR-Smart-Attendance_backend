using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAttendance.Application.Authentication;
using SmartAttendance.Application.Reports;
using SmartAttendance.UnitTests.Authentication;

namespace SmartAttendance.UnitTests.Reports;

internal sealed class ReportEmailApiWebApplicationFactory(
    string role,
    string status,
    IAttendanceReportEmailService service) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IFirebaseIdTokenVerifier>();
            services.RemoveAll<IUserProfileReader>();
            services.RemoveAll<IAttendanceReportEmailService>();
            services.AddSingleton<IFirebaseIdTokenVerifier>(
                new FakeFirebaseIdTokenVerifier((_, _) => new FirebaseIdentity("caller-uid")));
            services.AddSingleton<IUserProfileReader>(
                new FakeUserProfileReader(new UserProfile("caller-uid", role, status)));
            services.AddSingleton(service);
        });
    }
}

internal sealed class StubReportEmailService : IAttendanceReportEmailService
{
    public SendAttendanceReportCommand? Command { get; private set; }
    public Exception? Exception { get; set; }

    public Task<SendAttendanceReportResult> SendAsync(
        SendAttendanceReportCommand command,
        CancellationToken cancellationToken)
    {
        Command = command;
        if (Exception is not null)
        {
            return Task.FromException<SendAttendanceReportResult>(Exception);
        }
        return Task.FromResult(new SendAttendanceReportResult(
            "request-id",
            "accepted",
            command.EmployeeUid,
            command.PeriodKind,
            "2026-10-01",
            "2026-10-31",
            "e***@example.test",
            new DateTimeOffset(2026, 10, 28, 8, 0, 0, TimeSpan.Zero)));
    }
}

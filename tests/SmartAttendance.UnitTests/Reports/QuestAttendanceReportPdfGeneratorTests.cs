using SmartAttendance.Application.Reports;
using SmartAttendance.Infrastructure.Reporting.Pdf;

namespace SmartAttendance.UnitTests.Reports;

public sealed class QuestAttendanceReportPdfGeneratorTests
{
    [Fact]
    public void GeneratesBoundedPdfWithUnicodeAndNfcMobileData()
    {
        var generator = new QuestAttendanceReportPdfGenerator();
        var period = new ReportPeriod(
            ReportPeriodKind.Month,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31));

        var report = generator.Generate(
            ReportTestData.Source(),
            period,
            new DateTimeOffset(2026, 10, 28, 8, 0, 0, TimeSpan.Zero));

        Assert.True(report.Bytes.AsSpan().StartsWith("%PDF"u8));
        Assert.InRange(report.Bytes.Length, 1, AttendanceReportEmailService.MaxPdfBytes);
        Assert.Equal(1, report.Statistics.NfcRecords);
        Assert.Equal(480, report.Statistics.TotalMinutes);
    }
}

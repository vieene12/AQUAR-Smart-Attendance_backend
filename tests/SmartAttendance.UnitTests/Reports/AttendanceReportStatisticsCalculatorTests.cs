using SmartAttendance.Application.Reports;

namespace SmartAttendance.UnitTests.Reports;

public sealed class AttendanceReportStatisticsCalculatorTests
{
    [Fact]
    public void MatchesFlutterLunchAndMethodRulesIncludingNfcMobile()
    {
        var source = ReportTestData.Source();
        var period = new ReportPeriod(
            ReportPeriodKind.Month,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31));

        var result = AttendanceReportStatisticsCalculator.Calculate(
            source.Records,
            source.Uid,
            period);

        Assert.Equal(480, result.TotalMinutes);
        Assert.Equal(1, result.CompletedRecords);
        Assert.Equal(1, result.NfcRecords);
        Assert.Equal(1, result.ManuallyAdjustedRecords);
    }

    [Fact]
    public void ConflictingSameDayRecordsAreInvalidAndNotCounted()
    {
        var source = ReportTestData.Source();
        var conflict = source.Records[0] with { Method = "biometric" };
        var period = new ReportPeriod(
            ReportPeriodKind.Month,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31));

        var result = AttendanceReportStatisticsCalculator.Calculate(
            [source.Records[0], conflict],
            source.Uid,
            period);

        Assert.Equal(0, result.AttendanceDays);
        Assert.Equal(1, result.InvalidRecords);
    }
}

namespace SmartAttendance.Api.Reports;

public sealed record SendAttendanceReportEmailRequest(
    string PeriodKind,
    string AnchorDate);

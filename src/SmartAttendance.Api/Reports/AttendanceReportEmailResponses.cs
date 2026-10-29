using SmartAttendance.Application.Reports;

namespace SmartAttendance.Api.Reports;

public sealed record SendAttendanceReportEmailResponse(
    string RequestId,
    string Status,
    string EmployeeUid,
    string PeriodKind,
    string StartDate,
    string EndDate,
    string MaskedRecipient,
    DateTimeOffset AcceptedAtUtc)
{
    public static SendAttendanceReportEmailResponse FromDomain(
        SendAttendanceReportResult result) => new(
            result.RequestId,
            result.Status,
            result.EmployeeUid,
            result.PeriodKind,
            result.StartDate,
            result.EndDate,
            result.MaskedRecipient,
            result.AcceptedAtUtc);
}

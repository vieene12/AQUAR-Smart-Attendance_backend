namespace SmartAttendance.Application.Reports;

public enum ReportPeriodKind
{
    Week,
    Month,
    Quarter,
    Year,
}

public sealed record ReportPeriod(
    ReportPeriodKind Kind,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record AttendanceReportRecord(
    string EmployeeUid,
    string? WorkDate,
    string? Status,
    string? Method,
    DateTimeOffset? CheckInAt,
    DateTimeOffset? CheckOutAt,
    int? ManualRevision);

public sealed record EmployeeReportSource(
    string Uid,
    string FullName,
    string Email,
    IReadOnlyList<AttendanceReportRecord> Records);

public sealed record EmployeeReportRecipient(
    string Uid,
    string FullName,
    string Email);

public sealed record AttendanceReportStatistics(
    int CompletedRecords,
    int WorkingRecords,
    int TotalMinutes,
    int AttendanceDays,
    int BiometricRecords,
    int NfcRecords,
    int ManualRecords,
    int ManuallyAdjustedRecords,
    int InvalidRecords);

public sealed record AttendanceReportDocument(
    byte[] Bytes,
    string FileName,
    AttendanceReportStatistics Statistics);

public sealed record SendAttendanceReportCommand(
    string CallerUid,
    string EmployeeUid,
    string PeriodKind,
    string AnchorDate,
    string IdempotencyKey);

public sealed record SendAttendanceReportResult(
    string RequestId,
    string Status,
    string EmployeeUid,
    string PeriodKind,
    string StartDate,
    string EndDate,
    string MaskedRecipient,
    DateTimeOffset AcceptedAtUtc);

public sealed record ReportEmailMessage(
    string RequestId,
    string Recipient,
    string EmployeeName,
    ReportPeriod Period,
    string Subject,
    string Body,
    string AttachmentFileName,
    byte[] AttachmentBytes);

public sealed record ReportEmailProviderResult(string ProviderMessageId);

public enum ReportRequestStatus
{
    Processing,
    Accepted,
    Failed,
    Unknown,
}

public sealed record ReportRequestReservation(
    string RequestId,
    string CallerUid,
    string EmployeeUid,
    string PeriodKey,
    string RecipientHash,
    string PayloadHash,
    DateTimeOffset RequestedAtUtc);

public enum ReportReservationOutcomeKind
{
    Acquired,
    ExistingAccepted,
    ExistingProcessing,
    ExistingFailed,
    ExistingUnknown,
}

public sealed record ReportReservationOutcome(
    ReportReservationOutcomeKind Kind,
    DateTimeOffset? AcceptedAtUtc = null);

public interface IAttendanceReportReader
{
    Task<EmployeeReportRecipient> LoadRecipientAsync(
        string employeeUid,
        CancellationToken cancellationToken);

    Task<EmployeeReportSource> LoadAttendanceAsync(
        EmployeeReportRecipient recipient,
        ReportPeriod period,
        CancellationToken cancellationToken);
}

public interface IReportEmailOperationLogger
{
    void StageCompleted(string stage, long elapsedMilliseconds);

    void StageFailed(
        string stage,
        long elapsedMilliseconds,
        string failureType,
        bool canceled);
}

public interface IAttendanceReportPdfGenerator
{
    AttendanceReportDocument Generate(
        EmployeeReportSource source,
        ReportPeriod period,
        DateTimeOffset generatedAtUtc);
}

public interface IReportEmailSender
{
    Task<ReportEmailProviderResult> SendAsync(
        ReportEmailMessage message,
        CancellationToken cancellationToken);
}

public interface IReportEmailRequestStore
{
    Task<ReportReservationOutcome> TryReserveAsync(
        ReportRequestReservation reservation,
        CancellationToken cancellationToken);

    Task MarkAcceptedAsync(
        string requestId,
        string providerMessageId,
        DateTimeOffset acceptedAtUtc,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(
        string requestId,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken);

    Task MarkUnknownAsync(
        string requestId,
        DateTimeOffset unknownAtUtc,
        CancellationToken cancellationToken);
}

public interface IReportClock
{
    DateTimeOffset UtcNow { get; }
}

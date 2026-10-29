using SmartAttendance.Application.Reports;

namespace SmartAttendance.UnitTests.Reports;

internal sealed class FakeReportReader(EmployeeReportSource source)
    : IAttendanceReportReader
{
    public Task<EmployeeReportRecipient> LoadRecipientAsync(
        string employeeUid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new EmployeeReportRecipient(
            source.Uid,
            source.FullName,
            source.Email));
    }

    public Task<EmployeeReportSource> LoadAttendanceAsync(
        EmployeeReportRecipient recipient,
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(source);
    }
}

internal sealed class FakePdfGenerator(byte[]? bytes = null)
    : IAttendanceReportPdfGenerator
{
    public AttendanceReportDocument Generate(
        EmployeeReportSource source,
        ReportPeriod period,
        DateTimeOffset generatedAtUtc) => new(
            bytes ?? "%PDF-test"u8.ToArray(),
            "report.pdf",
            new AttendanceReportStatistics(1, 0, 480, 1, 1, 0, 0, 0, 0));
}

internal sealed class FakeEmailSender : IReportEmailSender
{
    public int Calls { get; private set; }
    public ReportEmailMessage? LastMessage { get; private set; }
    public Exception? Exception { get; set; }

    public Task<ReportEmailProviderResult> SendAsync(
        ReportEmailMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        LastMessage = message;
        return Exception is null
            ? Task.FromResult(new ReportEmailProviderResult("provider-id"))
            : Task.FromException<ReportEmailProviderResult>(Exception);
    }
}

internal sealed class FakeReportRequestStore : IReportEmailRequestStore
{
    private readonly Dictionary<string, (string Payload, ReportRequestStatus Status, DateTimeOffset? Accepted)> _requests = [];

    public int ReservationCalls { get; private set; }
    public int MarkAcceptedCalls { get; private set; }
    public int MarkFailedCalls { get; private set; }
    public int MarkUnknownCalls { get; private set; }

    public ReportRequestStatus? LastStatus => _requests.Count == 0
        ? null
        : _requests.Values.Last().Status;

    public Task<ReportReservationOutcome> TryReserveAsync(
        ReportRequestReservation reservation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReservationCalls++;
        if (!_requests.TryGetValue(reservation.RequestId, out var existing))
        {
            _requests[reservation.RequestId] = (
                reservation.PayloadHash,
                ReportRequestStatus.Processing,
                null);
            return Task.FromResult(new ReportReservationOutcome(
                ReportReservationOutcomeKind.Acquired));
        }
        if (existing.Payload != reservation.PayloadHash)
        {
            throw new ReportRequestConflictException("Different payload.");
        }
        return Task.FromResult<ReportReservationOutcome>(existing.Status switch
        {
            ReportRequestStatus.Accepted => new(
                ReportReservationOutcomeKind.ExistingAccepted,
                existing.Accepted),
            ReportRequestStatus.Failed => new(ReportReservationOutcomeKind.ExistingFailed),
            ReportRequestStatus.Unknown => new(ReportReservationOutcomeKind.ExistingUnknown),
            _ => new(ReportReservationOutcomeKind.ExistingProcessing),
        });
    }

    public Task MarkAcceptedAsync(
        string requestId,
        string providerMessageId,
        DateTimeOffset acceptedAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MarkAcceptedCalls++;
        var request = _requests[requestId];
        _requests[requestId] = (request.Payload, ReportRequestStatus.Accepted, acceptedAtUtc);
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(
        string requestId,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MarkFailedCalls++;
        var request = _requests[requestId];
        _requests[requestId] = (request.Payload, ReportRequestStatus.Failed, null);
        return Task.CompletedTask;
    }

    public Task MarkUnknownAsync(
        string requestId,
        DateTimeOffset unknownAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MarkUnknownCalls++;
        var request = _requests[requestId];
        _requests[requestId] = (request.Payload, ReportRequestStatus.Unknown, null);
        return Task.CompletedTask;
    }
}

internal sealed class FakeReportClock(DateTimeOffset now) : IReportClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

internal sealed class FakeReportEmailOperationLogger : IReportEmailOperationLogger
{
    public List<string> CompletedStages { get; } = [];
    public List<(string Stage, string FailureType, bool Canceled)> FailedStages { get; } = [];

    public void StageCompleted(string stage, long elapsedMilliseconds) =>
        CompletedStages.Add(stage);

    public void StageFailed(
        string stage,
        long elapsedMilliseconds,
        string failureType,
        bool canceled) =>
        FailedStages.Add((stage, failureType, canceled));
}

internal static class ReportTestData
{
    public static EmployeeReportSource Source(string email = "employee@example.test") => new(
        "employee-uid",
        "Nguyễn Văn A",
        email,
        [new AttendanceReportRecord(
            "employee-uid",
            "2026-10-05",
            "completed",
            "nfc_mobile",
            new DateTimeOffset(2026, 10, 5, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero),
            1)]);
}

using System.Globalization;
using System.Diagnostics;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace SmartAttendance.Application.Reports;

public interface IAttendanceReportEmailService
{
    Task<SendAttendanceReportResult> SendAsync(
        SendAttendanceReportCommand command,
        CancellationToken cancellationToken);
}

public sealed class AttendanceReportEmailService(
    IAttendanceReportReader reader,
    IAttendanceReportPdfGenerator pdfGenerator,
    IReportEmailRequestStore requestStore,
    IReportEmailSender emailSender,
    IReportClock clock,
    IReportEmailOperationLogger operationLogger) : IAttendanceReportEmailService
{
    public const int MaxPdfBytes = 5 * 1024 * 1024;
    private static readonly TimeSpan ProviderOperationTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StatusPersistenceTimeout = TimeSpan.FromSeconds(15);

    public async Task<SendAttendanceReportResult> SendAsync(
        SendAttendanceReportCommand command,
        CancellationToken cancellationToken)
    {
        var period = ParsePeriod(command.PeriodKind, command.AnchorDate, clock.UtcNow);
        if (!Guid.TryParseExact(command.IdempotencyKey, "D", out var idempotencyGuid))
        {
            throw new ReportValidationException(
                "Idempotency-Key must be a canonical UUID.");
        }

        ValidateUid(command.CallerUid, nameof(command.CallerUid));
        ValidateUid(command.EmployeeUid, nameof(command.EmployeeUid));

        var recipient = await MeasureAsync(
                "recipient-profile",
                () => reader.LoadRecipientAsync(command.EmployeeUid, cancellationToken))
            .ConfigureAwait(false);
        ValidateEmail(recipient.Email);

        var requestId = Hash($"{command.CallerUid}\n{idempotencyGuid:D}");
        var periodKey = $"{command.EmployeeUid}:{period.Kind}:{period.StartDate:yyyy-MM-dd}:{period.EndDate:yyyy-MM-dd}";
        var recipientHash = Hash(recipient.Email.Trim().ToLowerInvariant());
        var payloadHash = Hash(
            $"{command.CallerUid}\n{command.EmployeeUid}\n{period.Kind}\n{period.StartDate:yyyy-MM-dd}\n{period.EndDate:yyyy-MM-dd}");
        var requestedAt = clock.UtcNow;
        var reservation = await MeasureAsync(
                "idempotency-reservation",
                () => requestStore.TryReserveAsync(
                    new ReportRequestReservation(
                        requestId,
                        command.CallerUid,
                        command.EmployeeUid,
                        periodKey,
                        recipientHash,
                        payloadHash,
                        requestedAt),
                    cancellationToken))
            .ConfigureAwait(false);

        if (reservation.Kind == ReportReservationOutcomeKind.ExistingAccepted)
        {
            return Result(
                requestId,
                recipient,
                period,
                reservation.AcceptedAtUtc ?? requestedAt);
        }
        if (reservation.Kind is ReportReservationOutcomeKind.ExistingProcessing or
            ReportReservationOutcomeKind.ExistingUnknown)
        {
            throw new ReportRequestConflictException(
                "This idempotent request is already processing or has an unknown delivery result.");
        }
        if (reservation.Kind == ReportReservationOutcomeKind.ExistingFailed)
        {
            throw new ReportRequestConflictException(
                "This idempotent request previously failed and will not be sent again.");
        }

        EmployeeReportSource source;
        AttendanceReportDocument document;
        try
        {
            source = await MeasureAsync(
                    "attendance-read",
                    () => reader.LoadAttendanceAsync(
                        recipient,
                        period,
                        cancellationToken))
                .ConfigureAwait(false);
            document = Measure(
                "pdf-generation",
                () => pdfGenerator.Generate(source, period, clock.UtcNow));
            if (document.Bytes.Length == 0)
            {
                throw new ReportHasNoDataException("The generated report is empty.");
            }
            if (document.Bytes.Length > MaxPdfBytes)
            {
                throw new ReportPayloadTooLargeException(
                    "The generated PDF exceeds the 5 MiB limit.");
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            await TryMarkFailedAsync(requestId).ConfigureAwait(false);
            throw;
        }

        var message = BuildMessage(requestId, source, period, document);
        try
        {
            using var providerTimeout = new CancellationTokenSource(ProviderOperationTimeout);
            var provider = await MeasureAsync(
                    "email-provider",
                    () => emailSender.SendAsync(message, providerTimeout.Token))
                .ConfigureAwait(false);
            var acceptedAt = clock.UtcNow;
            try
            {
                using var persistenceTimeout =
                    new CancellationTokenSource(StatusPersistenceTimeout);
                await MeasureAsync(
                        "status-accepted",
                        () => requestStore.MarkAcceptedAsync(
                            requestId,
                            provider.ProviderMessageId,
                            acceptedAt,
                            persistenceTimeout.Token))
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                throw new ReportEmailDeliveryUnknownException(
                    "The provider accepted the email but its durable status could not be recorded.",
                    exception);
            }

            return Result(requestId, recipient, period, acceptedAt);
        }
        catch (ReportEmailDeliveryUnknownException)
        {
            await TryMarkUnknownAsync(requestId).ConfigureAwait(false);
            throw;
        }
        catch (ReportEmailUnavailableException)
        {
            await TryMarkFailedAsync(requestId).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException exception)
        {
            await TryMarkUnknownAsync(requestId).ConfigureAwait(false);
            throw new ReportEmailDeliveryUnknownException(
                "The provider operation timed out; delivery state is unknown.",
                exception);
        }
    }

    public static ReportPeriod ParsePeriod(
        string kindValue,
        string anchorValue,
        DateTimeOffset nowUtc)
    {
        if (!Enum.TryParse<ReportPeriodKind>(kindValue, true, out var kind) ||
            !Enum.IsDefined(kind))
        {
            throw new ReportValidationException(
                "periodKind must be week, month, quarter, or year.");
        }
        if (!DateOnly.TryParseExact(
                anchorValue,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var anchor))
        {
            throw new ReportValidationException("anchorDate must use yyyy-MM-dd.");
        }

        var vietnamToday = DateOnly.FromDateTime(
            nowUtc.ToOffset(TimeSpan.FromHours(7)).DateTime);
        if (anchor > vietnamToday)
        {
            throw new ReportValidationException("anchorDate cannot be in the future.");
        }

        DateOnly start;
        DateOnly end;
        switch (kind)
        {
            case ReportPeriodKind.Week:
                start = anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7));
                end = start.AddDays(6);
                break;
            case ReportPeriodKind.Month:
                start = new DateOnly(anchor.Year, anchor.Month, 1);
                end = start.AddMonths(1).AddDays(-1);
                break;
            case ReportPeriodKind.Quarter:
                var firstMonth = ((anchor.Month - 1) / 3 * 3) + 1;
                start = new DateOnly(anchor.Year, firstMonth, 1);
                end = start.AddMonths(3).AddDays(-1);
                break;
            case ReportPeriodKind.Year:
                start = new DateOnly(anchor.Year, 1, 1);
                end = new DateOnly(anchor.Year, 12, 31);
                break;
            default:
                throw new ReportValidationException("Unsupported periodKind.");
        }
        return new ReportPeriod(kind, start, end);
    }

    private async Task TryMarkUnknownAsync(string requestId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(StatusPersistenceTimeout);
            await MeasureAsync(
                    "status-unknown",
                    () => requestStore.MarkUnknownAsync(
                        requestId,
                        clock.UtcNow,
                        timeout.Token))
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The original request remains processing, which is fail-closed for retries.
        }
    }

    private async Task TryMarkFailedAsync(string requestId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(StatusPersistenceTimeout);
            await MeasureAsync(
                    "status-failed",
                    () => requestStore.MarkFailedAsync(
                        requestId,
                        clock.UtcNow,
                        timeout.Token))
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The original request remains processing, which is fail-closed for retries.
        }
    }

    private async Task<T> MeasureAsync<T>(string stage, Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await action().ConfigureAwait(false);
            operationLogger.StageCompleted(stage, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (Exception exception)
        {
            operationLogger.StageFailed(
                stage,
                stopwatch.ElapsedMilliseconds,
                exception.GetType().Name,
                exception is OperationCanceledException);
            throw;
        }
    }

    private async Task MeasureAsync(string stage, Func<Task> action)
    {
        await MeasureAsync(
                stage,
                async () =>
                {
                    await action().ConfigureAwait(false);
                    return true;
                })
            .ConfigureAwait(false);
    }

    private T Measure<T>(string stage, Func<T> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = action();
            operationLogger.StageCompleted(stage, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (Exception exception)
        {
            operationLogger.StageFailed(
                stage,
                stopwatch.ElapsedMilliseconds,
                exception.GetType().Name,
                canceled: false);
            throw;
        }
    }

    private static ReportEmailMessage BuildMessage(
        string requestId,
        EmployeeReportSource source,
        ReportPeriod period,
        AttendanceReportDocument document)
    {
        var periodName = period.Kind switch
        {
            ReportPeriodKind.Week => "tuần",
            ReportPeriodKind.Month => "tháng",
            ReportPeriodKind.Quarter => "quý",
            ReportPeriodKind.Year => "năm",
            _ => "kỳ",
        };
        var range = $"{period.StartDate:yyyy-MM-dd} – {period.EndDate:yyyy-MM-dd}";
        return new ReportEmailMessage(
            requestId,
            source.Email.Trim(),
            source.FullName,
            period,
            $"Báo cáo chấm công của {source.FullName} - {periodName} {range}",
            $"Kính gửi {source.FullName},\n\nĐính kèm là báo cáo chấm công của bạn cho {periodName} {range}.\nVui lòng kiểm tra nội dung báo cáo PDF.\n\nTrân trọng,\nSmart Attendance",
            document.FileName,
            document.Bytes);
    }

    private static SendAttendanceReportResult Result(
        string requestId,
        EmployeeReportRecipient recipient,
        ReportPeriod period,
        DateTimeOffset acceptedAt) => new(
            requestId,
            "accepted",
            recipient.Uid,
            period.Kind.ToString().ToLowerInvariant(),
            period.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            period.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            MaskEmail(recipient.Email),
            acceptedAt);

    private static void ValidateUid(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            value.Any(character => char.IsControl(character) || character == '/'))
        {
            throw new ReportValidationException($"{name} is invalid.");
        }
    }

    private static void ValidateEmail(string value)
    {
        var trimmed = value.Trim();
        try
        {
            var parsed = new MailAddress(trimmed);
            if (trimmed.Length > 254 ||
                !string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException();
            }
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ReportEmailInvalidException(
                "The employee registered email is missing or invalid.");
        }
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0) return "***";
        return $"{email[0]}***{email[at..]}";
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class SystemReportClock : IReportClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

using Google.Cloud.Firestore;
using Grpc.Core;
using SmartAttendance.Application.Authentication;
using SmartAttendance.Application.Reports;

namespace SmartAttendance.Infrastructure.Firebase.Reports;

internal sealed class FirestoreReportEmailRequestStore(FirestoreDb firestoreDb)
    : IReportEmailRequestStore
{
    private const string RequestCollection = "report_email_requests";
    private const string RateCollection = "report_email_rate_limits";

    public async Task<ReportReservationOutcome> TryReserveAsync(
        ReportRequestReservation reservation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await firestoreDb.RunTransactionAsync(
                    async transaction =>
                    {
                        var requestReference = firestoreDb.Collection(RequestCollection)
                            .Document(reservation.RequestId);
                        var callerReference = firestoreDb.Collection(RateCollection)
                            .Document($"caller_{HashId(reservation.CallerUid)}");
                        var recipientReference = firestoreDb.Collection(RateCollection)
                            .Document($"recipient_{reservation.RecipientHash}");
                        var periodReference = firestoreDb.Collection(RateCollection)
                            .Document($"period_{HashId(reservation.PeriodKey)}");

                        var snapshots = await transaction.GetAllSnapshotsAsync(
                                [requestReference, callerReference, recipientReference, periodReference],
                                cancellationToken)
                            .ConfigureAwait(false);
                        var request = snapshots[0];
                        if (request.Exists)
                        {
                            return ExistingOutcome(request, reservation.PayloadHash);
                        }

                        var now = reservation.RequestedAtUtc;
                        var callerEvents = RecentEvents(snapshots[1], now.AddMinutes(-1));
                        var recipientEvents = RecentEvents(snapshots[2], now.AddHours(-1));
                        if (callerEvents.Count >= 5 || recipientEvents.Count >= 3)
                        {
                            throw new ReportRateLimitException(
                                "The report email rate limit has been reached.");
                        }

                        var periodData = snapshots[3].Exists
                            ? snapshots[3].ToDictionary()
                            : null;
                        if (periodData is not null &&
                            periodData.TryGetValue("blockedUntil", out var blockedValue) &&
                            blockedValue is Timestamp blockedUntil &&
                            blockedUntil.ToDateTimeOffset() > now)
                        {
                            throw new ReportRateLimitException(
                                "A report was already requested for this employee and period recently.");
                        }

                        transaction.Create(requestReference, new Dictionary<string, object>
                        {
                            ["callerUid"] = reservation.CallerUid,
                            ["employeeUid"] = reservation.EmployeeUid,
                            ["periodKey"] = reservation.PeriodKey,
                            ["recipientHash"] = reservation.RecipientHash,
                            ["payloadHash"] = reservation.PayloadHash,
                            ["status"] = "processing",
                            ["requestedAt"] = Timestamp.FromDateTimeOffset(now),
                            ["updatedAt"] = Timestamp.FromDateTimeOffset(now),
                        });
                        transaction.Set(callerReference, RateData(callerEvents, now));
                        transaction.Set(recipientReference, RateData(recipientEvents, now));
                        transaction.Set(periodReference, new Dictionary<string, object>
                        {
                            ["requestId"] = reservation.RequestId,
                            ["blockedUntil"] = Timestamp.FromDateTimeOffset(now.AddMinutes(10)),
                            ["updatedAt"] = Timestamp.FromDateTimeOffset(now),
                        });
                        return new ReportReservationOutcome(
                            ReportReservationOutcomeKind.Acquired);
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw new FirebaseServiceUnavailableException(
                "Cloud Firestore is unavailable.",
                exception);
        }
    }

    public Task MarkAcceptedAsync(
        string requestId,
        string providerMessageId,
        DateTimeOffset acceptedAtUtc,
        CancellationToken cancellationToken) =>
        UpdateStatusAsync(
            requestId,
            "accepted",
            acceptedAtUtc,
            new Dictionary<string, object>
            {
                ["providerMessageId"] = providerMessageId,
                ["acceptedAt"] = Timestamp.FromDateTimeOffset(acceptedAtUtc),
            },
            releasePeriod: false,
            cancellationToken);

    public Task MarkFailedAsync(
        string requestId,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken) =>
        UpdateStatusAsync(
            requestId,
            "failed",
            failedAtUtc,
            new Dictionary<string, object>
            {
                ["failedAt"] = Timestamp.FromDateTimeOffset(failedAtUtc),
            },
            releasePeriod: true,
            cancellationToken);

    public Task MarkUnknownAsync(
        string requestId,
        DateTimeOffset unknownAtUtc,
        CancellationToken cancellationToken) =>
        UpdateStatusAsync(
            requestId,
            "unknown",
            unknownAtUtc,
            new Dictionary<string, object>
            {
                ["unknownAt"] = Timestamp.FromDateTimeOffset(unknownAtUtc),
            },
            releasePeriod: false,
            cancellationToken);

    private async Task UpdateStatusAsync(
        string requestId,
        string status,
        DateTimeOffset now,
        Dictionary<string, object> fields,
        bool releasePeriod,
        CancellationToken cancellationToken)
    {
        try
        {
            await firestoreDb.RunTransactionAsync(
                    async transaction =>
                    {
                        var requestReference = firestoreDb.Collection(RequestCollection)
                            .Document(requestId);
                        var request = await transaction.GetSnapshotAsync(
                                requestReference,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (!request.Exists)
                        {
                            throw new FirebaseServiceUnavailableException(
                                "The durable email request state is missing.");
                        }

                        DocumentReference? periodReference = null;
                        DocumentSnapshot? period = null;
                        if (releasePeriod)
                        {
                            var data = request.ToDictionary();
                            if (data.TryGetValue("periodKey", out var periodValue) &&
                                periodValue is string periodKey)
                            {
                                periodReference = firestoreDb.Collection(RateCollection)
                                    .Document($"period_{HashId(periodKey)}");
                                period = await transaction.GetSnapshotAsync(
                                        periodReference,
                                        cancellationToken)
                                    .ConfigureAwait(false);
                            }
                        }

                        fields["status"] = status;
                        fields["updatedAt"] = Timestamp.FromDateTimeOffset(now);
                        transaction.Update(requestReference, fields);
                        if (periodReference is not null &&
                            period is not null &&
                            period.Exists &&
                            period.TryGetValue<string>("requestId", out var owner) &&
                            owner == requestId)
                        {
                            transaction.Update(periodReference, new Dictionary<string, object>
                            {
                                ["blockedUntil"] = Timestamp.FromDateTimeOffset(now),
                                ["updatedAt"] = Timestamp.FromDateTimeOffset(now),
                            });
                        }
                        return true;
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw new FirebaseServiceUnavailableException(
                "Cloud Firestore is unavailable.",
                exception);
        }
    }

    private static ReportReservationOutcome ExistingOutcome(
        DocumentSnapshot request,
        string payloadHash)
    {
        var data = request.ToDictionary();
        if (!data.TryGetValue("payloadHash", out var hashValue) ||
            hashValue is not string storedHash ||
            storedHash != payloadHash)
        {
            throw new ReportRequestConflictException(
                "The Idempotency-Key was already used with a different payload.");
        }

        var status = data.TryGetValue("status", out var statusValue)
            ? statusValue as string
            : null;
        var acceptedAt = data.TryGetValue("acceptedAt", out var acceptedValue) &&
            acceptedValue is Timestamp timestamp
            ? timestamp.ToDateTimeOffset()
            : (DateTimeOffset?)null;
        return status switch
        {
            "accepted" => new(
                ReportReservationOutcomeKind.ExistingAccepted,
                acceptedAt),
            "failed" => new(ReportReservationOutcomeKind.ExistingFailed),
            "unknown" => new(ReportReservationOutcomeKind.ExistingUnknown),
            _ => new(ReportReservationOutcomeKind.ExistingProcessing),
        };
    }

    private static List<DateTimeOffset> RecentEvents(
        DocumentSnapshot snapshot,
        DateTimeOffset threshold)
    {
        if (!snapshot.Exists ||
            !snapshot.ToDictionary().TryGetValue("events", out var value) ||
            value is not IEnumerable<object> values)
        {
            return [];
        }
        return values
            .OfType<Timestamp>()
            .Select(timestamp => timestamp.ToDateTimeOffset())
            .Where(timestamp => timestamp > threshold)
            .OrderBy(timestamp => timestamp)
            .ToList();
    }

    private static Dictionary<string, object> RateData(
        List<DateTimeOffset> events,
        DateTimeOffset now)
    {
        events.Add(now);
        return new Dictionary<string, object>
        {
            ["events"] = events.Select(Timestamp.FromDateTimeOffset).ToArray(),
            ["updatedAt"] = Timestamp.FromDateTimeOffset(now),
        };
    }

    private static string HashId(string value) =>
        Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)));
}

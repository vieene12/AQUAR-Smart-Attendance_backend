using Google.Cloud.Firestore;
using Grpc.Core;
using System.Globalization;
using SmartAttendance.Application.Authentication;
using SmartAttendance.Application.Reports;

namespace SmartAttendance.Infrastructure.Firebase.Reports;

internal sealed class FirestoreAttendanceReportReader(FirestoreDb firestoreDb)
    : IAttendanceReportReader
{
    public async Task<EmployeeReportRecipient> LoadRecipientAsync(
        string employeeUid,
        CancellationToken cancellationToken)
    {
        try
        {
            var employee = await firestoreDb.Collection("users")
                .Document(employeeUid)
                .GetSnapshotAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!employee.Exists)
            {
                throw new ReportEmployeeNotFoundException("Employee does not exist.");
            }

            var profile = employee.ToDictionary();
            if (ReadString(profile, "role") != "employee" ||
                ReadString(profile, "status") != "active")
            {
                throw new ReportEmployeeNotFoundException(
                    "The target must be an active employee.");
            }

            return new EmployeeReportRecipient(
                employeeUid,
                ReadString(profile, "fullName") ?? employeeUid,
                ReadString(profile, "email") ?? string.Empty);
        }
        catch (RpcException exception)
        {
            throw new FirebaseServiceUnavailableException(
                "Cloud Firestore is unavailable.",
                exception);
        }
    }

    public async Task<EmployeeReportSource> LoadAttendanceAsync(
        EmployeeReportRecipient recipient,
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        try
        {
            var attendance = await firestoreDb.Collection("attendance")
                .WhereGreaterThanOrEqualTo(
                    "workDate",
                    period.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .WhereLessThanOrEqualTo(
                    "workDate",
                    period.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .GetSnapshotAsync(cancellationToken)
                .ConfigureAwait(false);

            var records = attendance.Documents
                .Select(ToRecord)
                .Where(record => record.EmployeeUid == recipient.Uid)
                .ToArray();
            return new EmployeeReportSource(
                recipient.Uid,
                recipient.FullName,
                recipient.Email,
                records);
        }
        catch (RpcException exception)
        {
            throw new FirebaseServiceUnavailableException(
                "Cloud Firestore is unavailable.",
                exception);
        }
    }

    private static AttendanceReportRecord ToRecord(DocumentSnapshot snapshot)
    {
        var data = snapshot.ToDictionary();
        return new AttendanceReportRecord(
            ReadString(data, "employeeId") ?? string.Empty,
            ReadString(data, "workDate"),
            ReadString(data, "status"),
            ReadString(data, "method"),
            ReadTimestamp(data, "checkInAt"),
            ReadTimestamp(data, "checkOutAt"),
            data.TryGetValue("manualRevision", out var revision) && revision is long value &&
                value is >= int.MinValue and <= int.MaxValue
                ? (int)value
                : null);
    }

    private static string? ReadString(
        Dictionary<string, object> data,
        string field) =>
        data.TryGetValue(field, out var value) && value is string text
            ? text.Trim()
            : null;

    private static DateTimeOffset? ReadTimestamp(
        Dictionary<string, object> data,
        string field) =>
        data.TryGetValue(field, out var value) && value is Timestamp timestamp
            ? new DateTimeOffset(timestamp.ToDateTime(), TimeSpan.Zero)
            : null;
}

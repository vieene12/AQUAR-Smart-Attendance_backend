using System.Globalization;

namespace SmartAttendance.Application.Reports;

public sealed class AttendanceReportStatisticsCalculator
{
    private static readonly HashSet<string> ValidMethods =
        ["biometric", "nfc", "nfc_mobile", "manual"];

    public static AttendanceReportStatistics Calculate(
        IEnumerable<AttendanceReportRecord> records,
        string employeeUid,
        ReportPeriod period)
    {
        var groups = records
            .Where(record => record.EmployeeUid == employeeUid)
            .Where(record => TryDate(record.WorkDate, out var date) &&
                date >= period.StartDate && date <= period.EndDate)
            .GroupBy(record => record.WorkDate!, StringComparer.Ordinal);

        var accumulator = new Accumulator();
        foreach (var group in groups)
        {
            var unique = group.Distinct().ToArray();
            if (unique.Length != 1)
            {
                accumulator.InvalidRecords++;
                continue;
            }

            var record = unique[0];
            var revisionValid = record.ManualRevision is null or > 0;
            var completedMinutes = CalculateCompletedMinutes(record);
            var isCompleted = revisionValid && completedMinutes.HasValue;
            var isWorking = revisionValid &&
                record.Status == "working" &&
                record.Method is not null &&
                ValidMethods.Contains(record.Method) &&
                record.CheckInAt.HasValue &&
                VietnamDate(record.CheckInAt.Value) == record.WorkDate &&
                record.CheckOutAt is null;

            if (!isCompleted && !isWorking)
            {
                accumulator.InvalidRecords++;
                continue;
            }

            accumulator.AttendanceDays++;
            if (isCompleted)
            {
                accumulator.CompletedRecords++;
                accumulator.TotalMinutes += completedMinutes!.Value;
            }
            else
            {
                accumulator.WorkingRecords++;
            }

            switch (record.Method)
            {
                case "biometric": accumulator.BiometricRecords++; break;
                case "nfc":
                case "nfc_mobile": accumulator.NfcRecords++; break;
                case "manual": accumulator.ManualRecords++; break;
            }

            if (record.ManualRevision.HasValue)
            {
                accumulator.ManuallyAdjustedRecords++;
            }
        }

        return accumulator.ToStatistics();
    }

    private static int? CalculateCompletedMinutes(AttendanceReportRecord record)
    {
        if (record.Status != "completed" ||
            record.Method is null ||
            !ValidMethods.Contains(record.Method) ||
            !record.CheckInAt.HasValue ||
            !record.CheckOutAt.HasValue ||
            record.CheckOutAt <= record.CheckInAt ||
            VietnamDate(record.CheckInAt.Value) != record.WorkDate ||
            VietnamDate(record.CheckOutAt.Value) != record.WorkDate ||
            !TryDate(record.WorkDate, out var date))
        {
            return null;
        }

        var checkIn = record.CheckInAt.Value.UtcDateTime;
        var checkOut = record.CheckOutAt.Value.UtcDateTime;
        var lunchStart = new DateTime(date.Year, date.Month, date.Day, 5, 0, 0, DateTimeKind.Utc);
        var lunchEnd = lunchStart.AddHours(1);
        var overlapStart = checkIn > lunchStart ? checkIn : lunchStart;
        var overlapEnd = checkOut < lunchEnd ? checkOut : lunchEnd;
        var lunch = overlapEnd > overlapStart ? overlapEnd - overlapStart : TimeSpan.Zero;
        return (int)(checkOut - checkIn - lunch).TotalMinutes;
    }

    private static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    private static string VietnamDate(DateTimeOffset instant) =>
        instant.ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed class Accumulator
    {
        public int CompletedRecords { get; set; }
        public int WorkingRecords { get; set; }
        public int TotalMinutes { get; set; }
        public int AttendanceDays { get; set; }
        public int BiometricRecords { get; set; }
        public int NfcRecords { get; set; }
        public int ManualRecords { get; set; }
        public int ManuallyAdjustedRecords { get; set; }
        public int InvalidRecords { get; set; }

        public AttendanceReportStatistics ToStatistics() => new(
            CompletedRecords,
            WorkingRecords,
            TotalMinutes,
            AttendanceDays,
            BiometricRecords,
            NfcRecords,
            ManualRecords,
            ManuallyAdjustedRecords,
            InvalidRecords);
    }
}

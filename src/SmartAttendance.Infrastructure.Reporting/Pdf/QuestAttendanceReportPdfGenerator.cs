using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartAttendance.Application.Reports;

namespace SmartAttendance.Infrastructure.Reporting.Pdf;

public sealed class QuestAttendanceReportPdfGenerator : IAttendanceReportPdfGenerator
{
    private static readonly string[] TableHeaders =
        ["Ngày", "Giờ vào", "Giờ ra", "Trạng thái", "Phương thức", "Điều chỉnh"];

    static QuestAttendanceReportPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public AttendanceReportDocument Generate(
        EmployeeReportSource source,
        ReportPeriod period,
        DateTimeOffset generatedAtUtc)
    {
        var statistics = AttendanceReportStatisticsCalculator.Calculate(
            source.Records,
            source.Uid,
            period);
        if (statistics.AttendanceDays == 0)
        {
            throw new ReportHasNoDataException(
                "There is no valid attendance data for this report.");
        }

        var rows = source.Records
            .Where(record => record.EmployeeUid == source.Uid &&
                DateOnly.TryParseExact(
                    record.WorkDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date) &&
                date >= period.StartDate && date <= period.EndDate)
            .OrderBy(record => record.WorkDate, StringComparer.Ordinal)
            .ToArray();

        var bytes = Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(style => style.FontFamily(Fonts.Arial).FontSize(9));
                page.Header().Column(column =>
                {
                    column.Item().Text("BÁO CÁO CHẤM CÔNG CÁ NHÂN").Bold().FontSize(16);
                    column.Item().Text($"Nhân viên: {source.FullName}");
                    column.Item().Text($"Email đăng ký: {source.Email}");
                    column.Item().Text($"Kỳ báo cáo: {PeriodLabel(period.Kind)}, {period.StartDate:yyyy-MM-dd} – {period.EndDate:yyyy-MM-dd}");
                    column.Item().Text($"Ngày xuất (giờ Việt Nam): {generatedAtUtc.ToOffset(TimeSpan.FromHours(7)):dd/MM/yyyy HH:mm}");
                });
                page.Content().PaddingVertical(12).Column(column =>
                {
                    column.Spacing(4);
                    column.Item().Text($"Số ngày có chấm công: {statistics.AttendanceDays}");
                    column.Item().Text($"Completed: {statistics.CompletedRecords}");
                    column.Item().Text($"Working / chưa Check-out: {statistics.WorkingRecords}");
                    column.Item().Text($"Tổng giờ công hợp lệ: {statistics.TotalMinutes / 60} giờ {statistics.TotalMinutes % 60} phút");
                    column.Item().Text($"Biometric: {statistics.BiometricRecords}; NFC: {statistics.NfcRecords}; Manual: {statistics.ManualRecords}");
                    column.Item().Text($"Bản ghi điều chỉnh thủ công: {statistics.ManuallyAdjustedRecords}");
                    if (statistics.InvalidRecords > 0)
                    {
                        column.Item().Text($"Dữ liệu không hợp lệ/xung đột: {statistics.InvalidRecords}");
                    }

                    column.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(1.2f);
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn(1.1f);
                            columns.RelativeColumn(1.1f);
                            columns.RelativeColumn();
                        });
                        table.Header(header =>
                        {
                            foreach (var title in TableHeaders)
                            {
                                header.Cell().Border(0.5f).Padding(3).Text(title).Bold();
                            }
                        });
                        foreach (var row in rows)
                        {
                            Cell(table, row.WorkDate ?? "—");
                            Cell(table, Time(row.CheckInAt));
                            Cell(table, Time(row.CheckOutAt));
                            Cell(table, Status(row.Status));
                            Cell(table, Method(row.Method));
                            Cell(table, row.ManualRevision is > 0 ? "Có" : "Không");
                        }
                    });
                });
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Smart Attendance · Trang ");
                    text.CurrentPageNumber();
                });
            });
        }).GeneratePdf();

        var fileName = $"bao_cao_cham_cong_{period.Kind.ToString().ToLowerInvariant()}_{period.StartDate:yyyy-MM-dd}_{period.EndDate:yyyy-MM-dd}.pdf";
        return new AttendanceReportDocument(bytes, fileName, statistics);
    }

    private static void Cell(TableDescriptor table, string text) =>
        table.Cell().Border(0.5f).Padding(3).Text(text);

    private static string Time(DateTimeOffset? value) =>
        value?.ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm", CultureInfo.InvariantCulture) ?? "—";

    private static string PeriodLabel(ReportPeriodKind kind) => kind switch
    {
        ReportPeriodKind.Week => "Tuần",
        ReportPeriodKind.Month => "Tháng",
        ReportPeriodKind.Quarter => "Quý",
        ReportPeriodKind.Year => "Năm",
        _ => "Kỳ",
    };

    private static string Status(string? value) => value switch
    {
        "completed" => "Completed",
        "working" => "Working",
        _ => "Không hợp lệ",
    };

    private static string Method(string? value) => value switch
    {
        "biometric" => "Biometric",
        "nfc" => "NFC",
        "nfc_mobile" => "NFC mobile",
        "manual" => "Manual",
        _ => "Không hợp lệ",
    };
}

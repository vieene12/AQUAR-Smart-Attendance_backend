namespace SmartAttendance.Application.Reports;

public abstract class AttendanceReportEmailException(string message) : Exception(message);

public sealed class ReportValidationException(string message)
    : AttendanceReportEmailException(message);

public sealed class ReportEmployeeNotFoundException(string message)
    : AttendanceReportEmailException(message);

public sealed class ReportEmailInvalidException(string message)
    : AttendanceReportEmailException(message);

public sealed class ReportHasNoDataException(string message)
    : AttendanceReportEmailException(message);

public sealed class ReportPayloadTooLargeException(string message)
    : AttendanceReportEmailException(message);

public sealed class ReportRequestConflictException(string message)
    : AttendanceReportEmailException(message);

public sealed class ReportRateLimitException(string message)
    : AttendanceReportEmailException(message);

public sealed class ReportEmailUnavailableException(string message, Exception? innerException = null)
    : AttendanceReportEmailException(message)
{
    public Exception? ProviderException { get; } = innerException;
}

public sealed class ReportEmailDeliveryUnknownException(string message, Exception? innerException = null)
    : AttendanceReportEmailException(message)
{
    public Exception? ProviderException { get; } = innerException;
}

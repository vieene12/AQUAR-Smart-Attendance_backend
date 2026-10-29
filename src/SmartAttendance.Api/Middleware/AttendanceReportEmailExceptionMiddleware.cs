using SmartAttendance.Application.Reports;

namespace SmartAttendance.Api.Middleware;

public sealed partial class AttendanceReportEmailExceptionMiddleware(
    RequestDelegate next,
    ILogger<AttendanceReportEmailExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (AttendanceReportEmailException exception)
        {
            var (status, title) = exception switch
            {
                ReportValidationException =>
                    (StatusCodes.Status400BadRequest, "Invalid report request"),
                ReportEmployeeNotFoundException =>
                    (StatusCodes.Status404NotFound, "Employee not found"),
                ReportRequestConflictException =>
                    (StatusCodes.Status409Conflict, "Report request conflict"),
                ReportPayloadTooLargeException =>
                    (StatusCodes.Status413PayloadTooLarge, "Report is too large"),
                ReportEmailInvalidException or ReportHasNoDataException =>
                    (StatusCodes.Status422UnprocessableEntity, "Report cannot be sent"),
                ReportRateLimitException =>
                    (StatusCodes.Status429TooManyRequests, "Report email rate limit exceeded"),
                ReportEmailUnavailableException or ReportEmailDeliveryUnknownException =>
                    (StatusCodes.Status503ServiceUnavailable, "Report email unavailable"),
                _ =>
                    (StatusCodes.Status500InternalServerError, "Report email failed"),
            };
            LogFailure(logger, status, exception.GetType().Name);
            context.Response.StatusCode = status;
            await Results.Problem(
                    statusCode: status,
                    title: title,
                    detail: exception.Message)
                .ExecuteAsync(context)
                .ConfigureAwait(false);
        }
    }

    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Warning,
        Message = "Report email request failed with HTTP {StatusCode} ({FailureType}).")]
    private static partial void LogFailure(
        ILogger logger,
        int statusCode,
        string failureType);
}

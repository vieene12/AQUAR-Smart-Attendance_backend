using SmartAttendance.Application.Authentication;

namespace SmartAttendance.Api.Middleware;

public sealed partial class FirebaseDependencyExceptionMiddleware(
    RequestDelegate next,
    ILogger<FirebaseDependencyExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (FirebaseServiceUnavailableException exception)
        {
            LogFirebaseUnavailable(logger, exception);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Firebase service unavailable")
                .ExecuteAsync(context)
                .ConfigureAwait(false);
        }
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "A Firebase dependency is unavailable.")]
    private static partial void LogFirebaseUnavailable(
        ILogger logger,
        Exception exception);
}

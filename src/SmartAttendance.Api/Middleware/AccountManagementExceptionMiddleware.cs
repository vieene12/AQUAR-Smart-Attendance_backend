using SmartAttendance.Application.Accounts;

namespace SmartAttendance.Api.Middleware;

public sealed class AccountManagementExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (AccountManagementException exception)
        {
            var (status, title) = exception switch
            {
                AccountValidationException =>
                    (StatusCodes.Status400BadRequest, "Invalid account request"),
                AccountNotFoundException =>
                    (StatusCodes.Status404NotFound, "Account not found"),
                AccountProtectedException =>
                    (StatusCodes.Status403Forbidden, "Protected account"),
                AccountConflictException =>
                    (StatusCodes.Status409Conflict, "Account conflict"),
                AccountSynchronizationException =>
                    (StatusCodes.Status503ServiceUnavailable, "Account synchronization failed"),
                _ => (StatusCodes.Status500InternalServerError, "Account operation failed"),
            };
            context.Response.StatusCode = status;
            await Results.Problem(
                    statusCode: status,
                    title: title,
                    detail: exception.Message)
                .ExecuteAsync(context)
                .ConfigureAwait(false);
        }
    }
}

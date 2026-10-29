using System.Net.Http.Headers;
using System.Security.Claims;
using System.Diagnostics;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SmartAttendance.Application.Authentication;

namespace SmartAttendance.Api.Authentication;

public sealed partial class FirebaseAuthenticationHandler(
    IOptionsMonitor<FirebaseAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceProvider serviceProvider)
    : AuthenticationHandler<FirebaseAuthenticationOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authorizationValues) ||
            !AuthenticationHeaderValue.TryParse(
                authorizationValues.ToString(),
                out var authorizationHeader) ||
            !string.Equals(
                authorizationHeader.Scheme,
                "Bearer",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(authorizationHeader.Parameter))
        {
            return AuthenticateResult.NoResult();
        }

        try
        {
            var tokenVerifier = serviceProvider
                .GetRequiredService<IFirebaseIdTokenVerifier>();
            var profileReader = serviceProvider
                .GetRequiredService<IUserProfileReader>();
            var stage = "firebase-token-verification";
            var stopwatch = Stopwatch.StartNew();
            FirebaseIdentity identity;
            try
            {
                identity = await tokenVerifier
                    .VerifyAsync(
                        authorizationHeader.Parameter,
                        checkRevoked: true,
                        Context.RequestAborted)
                    .ConfigureAwait(false);
                LogAuthenticationStage(Logger, stage, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception exception)
            {
                LogAuthenticationStageFailure(
                    Logger,
                    stage,
                    stopwatch.ElapsedMilliseconds,
                    exception.GetType().Name,
                    exception is OperationCanceledException);
                throw;
            }

            stage = "caller-profile";
            stopwatch.Restart();
            UserProfile? profile;
            try
            {
                profile = await profileReader
                    .FindByUidAsync(identity.Uid, Context.RequestAborted)
                    .ConfigureAwait(false);
                LogAuthenticationStage(Logger, stage, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception exception)
            {
                LogAuthenticationStageFailure(
                    Logger,
                    stage,
                    stopwatch.ElapsedMilliseconds,
                    exception.GetType().Name,
                    exception is OperationCanceledException);
                throw;
            }

            if (profile is null)
            {
                return AuthenticateResult.Fail("The user profile does not exist.");
            }

            if (!FirebaseSecurityValues.IsValidRole(profile.Role) ||
                !FirebaseSecurityValues.IsValidStatus(profile.Status))
            {
                return AuthenticateResult.Fail("The user profile is invalid.");
            }

            if (!string.Equals(
                    profile.Status,
                    FirebaseSecurityValues.ActiveStatus,
                    StringComparison.Ordinal))
            {
                return AuthenticateResult.Fail("The user account is locked.");
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, identity.Uid),
                new Claim(ClaimTypes.Role, profile.Role),
                new Claim(FirebaseClaimTypes.Status, profile.Status),
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }
        catch (InvalidFirebaseIdTokenException exception)
        {
            return AuthenticateResult.Fail(exception.Message);
        }
    }

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Information,
        Message = "Authentication stage {Stage} completed in {ElapsedMilliseconds} ms.")]
    private static partial void LogAuthenticationStage(
        ILogger logger,
        string stage,
        long elapsedMilliseconds);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Warning,
        Message = "Authentication stage {Stage} failed after {ElapsedMilliseconds} ms ({FailureType}, canceled: {Canceled}).")]
    private static partial void LogAuthenticationStageFailure(
        ILogger logger,
        string stage,
        long elapsedMilliseconds,
        string failureType,
        bool canceled);
}

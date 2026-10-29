using FirebaseAdmin.Auth;
using SmartAttendance.Application.Authentication;

namespace SmartAttendance.Infrastructure.Firebase.Authentication;

internal sealed class FirebaseAdminIdTokenVerifier(FirebaseAuth firebaseAuth)
    : IFirebaseIdTokenVerifier
{
    public async Task<FirebaseIdentity> VerifyAsync(
        string idToken,
        bool checkRevoked,
        CancellationToken cancellationToken)
    {
        try
        {
            var decodedToken = await firebaseAuth
                .VerifyIdTokenAsync(idToken, checkRevoked, cancellationToken)
                .ConfigureAwait(false);
            return new FirebaseIdentity(decodedToken.Uid);
        }
        catch (FirebaseAuthException exception)
            when (exception.AuthErrorCode == AuthErrorCode.CertificateFetchFailed)
        {
            throw new FirebaseServiceUnavailableException(
                "Firebase Authentication is unavailable.",
                exception);
        }
        catch (FirebaseAuthException exception)
        {
            throw new InvalidFirebaseIdTokenException(
                "The Firebase ID token is invalid.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new FirebaseServiceUnavailableException(
                "Firebase Authentication is unavailable.",
                exception);
        }
    }
}

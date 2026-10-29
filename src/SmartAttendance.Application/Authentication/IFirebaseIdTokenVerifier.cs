namespace SmartAttendance.Application.Authentication;

public interface IFirebaseIdTokenVerifier
{
    Task<FirebaseIdentity> VerifyAsync(
        string idToken,
        bool checkRevoked,
        CancellationToken cancellationToken);
}

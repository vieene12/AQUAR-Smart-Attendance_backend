using SmartAttendance.Application.Authentication;

namespace SmartAttendance.UnitTests.Authentication;

internal sealed class FakeFirebaseIdTokenVerifier(
    Func<string, bool, FirebaseIdentity> verify)
    : IFirebaseIdTokenVerifier
{
    public Task<FirebaseIdentity> VerifyAsync(
        string idToken,
        bool checkRevoked,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(verify(idToken, checkRevoked));
    }
}

internal sealed class FakeUserProfileReader(
    Func<string, UserProfile?> findProfile)
    : IUserProfileReader
{
    public FakeUserProfileReader(UserProfile? profile)
        : this(_ => profile)
    {
    }

    public Task<UserProfile?> FindByUidAsync(
        string uid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(findProfile(uid));
    }
}

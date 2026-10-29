using Google.Cloud.Firestore;
using Grpc.Core;
using SmartAttendance.Application.Authentication;

namespace SmartAttendance.Infrastructure.Firebase.Authentication;

internal sealed class FirestoreUserProfileReader(FirestoreDb firestoreDb) : IUserProfileReader
{
    public async Task<UserProfile?> FindByUidAsync(
        string uid,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await firestoreDb
                .Collection("users")
                .Document(uid)
                .GetSnapshotAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!snapshot.Exists)
            {
                return null;
            }

            var fields = snapshot.ToDictionary();
            var role = fields.TryGetValue("role", out var storedRole) &&
                storedRole is string roleValue
                ? roleValue
                : string.Empty;
            var status = fields.TryGetValue("status", out var storedStatus) &&
                storedStatus is string statusValue
                ? statusValue
                : string.Empty;
            return new UserProfile(uid, role, status);
        }
        catch (RpcException exception)
        {
            throw new FirebaseServiceUnavailableException(
                "Cloud Firestore is unavailable.",
                exception);
        }
    }
}

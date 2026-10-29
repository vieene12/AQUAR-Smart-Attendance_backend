using System.Text;
using FirebaseAdmin.Auth;
using Google.Cloud.Firestore;
using Grpc.Core;
using SmartAttendance.Application.Accounts;
using SmartAttendance.Application.Authentication;
using SmartAttendance.Domain.Accounts;

namespace SmartAttendance.Infrastructure.Firebase.Accounts;

internal sealed class FirebaseAccountStore(
    FirebaseAuth firebaseAuth,
    FirestoreDb firestore) : IAccountStore
{
    private const string UsersCollection = "users";

    public async Task<AccountPage> ListProfilesAsync(
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        try
        {
            Query query = firestore.Collection(UsersCollection)
                .WhereIn(
                    "role",
                    new[]
                    {
                        AccountRole.Admin,
                        AccountRole.Employee,
                        AccountRole.Manager,
                    })
                .OrderBy(FieldPath.DocumentId)
                .Limit(pageSize + 1);
            if (!string.IsNullOrWhiteSpace(pageToken))
            {
                query = query.StartAfter(DecodePageToken(pageToken));
            }

            var snapshot = await query.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var accounts = snapshot.Documents.Select(MapProfile).ToList();
            string? nextPageToken = null;
            if (accounts.Count > pageSize)
            {
                accounts.RemoveAt(accounts.Count - 1);
                nextPageToken = EncodePageToken(accounts[^1].Uid);
            }

            return new AccountPage(accounts, nextPageToken);
        }
        catch (AccountValidationException)
        {
            throw;
        }
        catch (RpcException exception)
        {
            throw new FirebaseServiceUnavailableException(
                "Cloud Firestore is unavailable.",
                exception);
        }
    }

    public async Task<ManagedAccount?> FindProfileAsync(
        string uid,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await firestore.Collection(UsersCollection)
                .Document(uid)
                .GetSnapshotAsync(cancellationToken)
                .ConfigureAwait(false);
            return snapshot.Exists ? MapProfile(snapshot) : null;
        }
        catch (RpcException exception)
        {
            throw new FirebaseServiceUnavailableException(
                "Cloud Firestore is unavailable.",
                exception);
        }
    }

    public async Task<AccountIdentity?> FindIdentityAsync(
        string uid,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await firebaseAuth.GetUserAsync(uid, cancellationToken).ConfigureAwait(false);
            return MapIdentity(user);
        }
        catch (FirebaseAuthException exception)
            when (exception.AuthErrorCode == AuthErrorCode.UserNotFound)
        {
            return null;
        }
        catch (Exception exception) when (IsFirebaseDependencyFailure(exception))
        {
            throw new FirebaseServiceUnavailableException(
                "Firebase Authentication is unavailable.",
                exception);
        }
    }

    public async Task<AccountIdentity> CreateIdentityAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await firebaseAuth.CreateUserAsync(
                new UserRecordArgs
                {
                    Email = email,
                    Password = password,
                    DisplayName = displayName,
                    Disabled = false,
                },
                cancellationToken).ConfigureAwait(false);
            return MapIdentity(user);
        }
        catch (FirebaseAuthException exception)
            when (exception.AuthErrorCode == AuthErrorCode.EmailAlreadyExists)
        {
            throw new AccountConflictException("Email is already in use.");
        }
        catch (Exception exception) when (IsFirebaseDependencyFailure(exception))
        {
            throw new FirebaseServiceUnavailableException(
                "Firebase Authentication is unavailable.",
                exception);
        }
    }

    public async Task CreateProfileAsync(
        ManagedAccount account,
        CancellationToken cancellationToken)
    {
        try
        {
            await firestore.Collection(UsersCollection).Document(account.Uid).CreateAsync(
                ToProfileData(account),
                cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.AlreadyExists)
        {
            throw new AccountConflictException("The Firestore profile already exists.");
        }
        catch (RpcException exception)
        {
            throw new FirebaseServiceUnavailableException(
                "Cloud Firestore is unavailable.",
                exception);
        }
    }

    public Task UpdateIdentityDisplayNameAsync(
        string uid,
        string displayName,
        CancellationToken cancellationToken) =>
        UpdateIdentityAsync(
            new UserRecordArgs { Uid = uid, DisplayName = displayName },
            cancellationToken);

    public Task SetIdentityDisabledAsync(
        string uid,
        bool disabled,
        CancellationToken cancellationToken) =>
        UpdateIdentityAsync(
            new UserRecordArgs { Uid = uid, Disabled = disabled },
            cancellationToken);

    public Task UpdateProfileAsync(
        string uid,
        string fullName,
        long hourlyRate,
        CancellationToken cancellationToken) =>
        UpdateProfileFieldsAsync(
            uid,
            new Dictionary<string, object>
            {
                ["fullName"] = fullName,
                ["hourlyRate"] = hourlyRate,
            },
            cancellationToken);

    public Task UpdateProfileStatusAsync(
        string uid,
        string status,
        CancellationToken cancellationToken) =>
        UpdateProfileFieldsAsync(
            uid,
            new Dictionary<string, object> { ["status"] = status },
            cancellationToken);

    private async Task UpdateIdentityAsync(
        UserRecordArgs args,
        CancellationToken cancellationToken)
    {
        try
        {
            await firebaseAuth.UpdateUserAsync(args, cancellationToken).ConfigureAwait(false);
        }
        catch (FirebaseAuthException exception)
            when (exception.AuthErrorCode == AuthErrorCode.UserNotFound)
        {
            throw new AccountConflictException(
                "The Firebase Authentication account no longer exists.");
        }
        catch (Exception exception) when (IsFirebaseDependencyFailure(exception))
        {
            throw new FirebaseServiceUnavailableException(
                "Firebase Authentication is unavailable.",
                exception);
        }
    }

    private async Task UpdateProfileFieldsAsync(
        string uid,
        Dictionary<string, object> fields,
        CancellationToken cancellationToken)
    {
        try
        {
            await firestore.Collection(UsersCollection).Document(uid)
                .UpdateAsync(fields, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.NotFound)
        {
            throw new AccountNotFoundException("Account profile was not found.");
        }
        catch (RpcException exception)
        {
            throw new FirebaseServiceUnavailableException(
                "Cloud Firestore is unavailable.",
                exception);
        }
    }

    private static ManagedAccount MapProfile(DocumentSnapshot snapshot)
    {
        var data = snapshot.ToDictionary();
        if (!TryString(data, "role", out var role) ||
            !TryString(data, "status", out var status) ||
            !AccountRole.IsValid(role) ||
            !AccountStatus.IsValid(status))
        {
            throw new AccountConflictException(
                $"Account profile {snapshot.Id} does not match the F02 schema.");
        }

        var hasFullName = TryString(data, "fullName", out var fullName);
        if (!hasFullName)
        {
            hasFullName = TryString(data, "name", out fullName);
        }

        var hasEmail = TryString(data, "email", out var email);
        var hourlyRate = data.TryGetValue("hourlyRate", out var storedHourlyRate) &&
            storedHourlyRate is long value
            ? value
            : (long?)null;
        var hasHourlyRate = hourlyRate >= 0;

        if (role is AccountRole.Employee or AccountRole.Manager &&
            (!hasFullName || !hasEmail || !hasHourlyRate))
        {
            throw new AccountConflictException(
                $"Account profile {snapshot.Id} does not match the F02 schema.");
        }

        return new ManagedAccount(
            snapshot.Id,
            hasFullName ? fullName : null,
            hasEmail ? email : null,
            role,
            status,
            hasHourlyRate ? hourlyRate : null);
    }

    private static AccountIdentity MapIdentity(UserRecord user) =>
        new(user.Uid, user.Email ?? string.Empty, user.DisplayName, user.Disabled);

    private static Dictionary<string, object> ToProfileData(ManagedAccount account) =>
        new()
        {
            ["fullName"] = account.FullName ?? throw new AccountValidationException(
                "Full name is required."),
            ["email"] = account.Email ?? throw new AccountValidationException(
                "Email is required."),
            ["role"] = account.Role,
            ["status"] = account.Status,
            ["hourlyRate"] = account.HourlyRate ?? throw new AccountValidationException(
                "Hourly rate is required."),
        };

    private static bool TryString(
        Dictionary<string, object> data,
        string field,
        out string value)
    {
        value = data.TryGetValue(field, out var stored) && stored is string text
            ? text.Trim()
            : string.Empty;
        return value.Length > 0;
    }

    private static string EncodePageToken(string uid) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(uid))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string DecodePageToken(string token)
    {
        try
        {
            var normalized = token.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(
                normalized.Length + ((4 - (normalized.Length % 4)) % 4),
                '=');
            var uid = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            if (string.IsNullOrWhiteSpace(uid))
            {
                throw new FormatException();
            }

            return uid;
        }
        catch (FormatException exception)
        {
            throw new AccountValidationException(
                $"Page token is invalid: {exception.Message}");
        }
    }

    private static bool IsFirebaseDependencyFailure(Exception exception) =>
        exception is FirebaseAuthException or HttpRequestException;
}

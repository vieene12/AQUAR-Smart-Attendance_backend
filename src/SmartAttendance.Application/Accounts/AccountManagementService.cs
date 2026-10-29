using SmartAttendance.Application.Authentication;
using SmartAttendance.Domain.Accounts;

namespace SmartAttendance.Application.Accounts;

public sealed class AccountManagementService(
    IAccountStore store,
    IAccountOperationLogger logger) : IAccountManagementService
{
    public Task<AccountPage> ListAsync(
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        AccountInputValidator.ValidatePageSize(pageSize);
        return store.ListProfilesAsync(pageSize, pageToken, cancellationToken);
    }

    public async Task<ManagedAccount> CreateAsync(
        string callerUid,
        CreateAccountCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCallerUid(callerUid);
        var input = AccountInputValidator.Validate(command);
        var identity = await store.CreateIdentityAsync(
            input.Email,
            input.Password,
            input.FullName,
            cancellationToken).ConfigureAwait(false);
        var account = new ManagedAccount(
            identity.Uid,
            input.FullName,
            input.Email,
            input.Role,
            AccountStatus.Active,
            input.HourlyRate);

        try
        {
            await store.CreateProfileAsync(account, cancellationToken).ConfigureAwait(false);
            return account;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (await ProfileMatchesAfterFailureAsync(account, cancellationToken)
                    .ConfigureAwait(false))
            {
                return account;
            }

            await DisableAfterCreateFailureAsync(identity.Uid, exception, cancellationToken)
                .ConfigureAwait(false);
            throw new AccountSynchronizationException(
                "Authentication was created but the Firestore profile could not be confirmed. " +
                "The Authentication account was disabled and requires reconciliation.");
        }
    }

    public async Task<ManagedAccount> UpdateAsync(
        string callerUid,
        string targetUid,
        UpdateAccountCommand command,
        CancellationToken cancellationToken)
    {
        ValidateTarget(callerUid, targetUid);
        var input = AccountInputValidator.Validate(command);
        var profile = await GetManageableProfileAsync(targetUid, cancellationToken)
            .ConfigureAwait(false);
        var identity = await GetIdentityAsync(targetUid, cancellationToken).ConfigureAwait(false);
        var oldDisplayName = identity.DisplayName;
        var displayNameChanged = !string.Equals(
            oldDisplayName,
            input.FullName,
            StringComparison.Ordinal);

        if (displayNameChanged)
        {
            await store.UpdateIdentityDisplayNameAsync(
                targetUid,
                input.FullName,
                cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await store.UpdateProfileAsync(
                targetUid,
                input.FullName,
                input.HourlyRate,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (displayNameChanged)
            {
                await TryRestoreDisplayNameAsync(
                    targetUid,
                    oldDisplayName ?? profile.FullName ?? string.Empty,
                    exception,
                    cancellationToken).ConfigureAwait(false);
            }

            throw;
        }

        return profile with
        {
            FullName = input.FullName,
            HourlyRate = input.HourlyRate,
        };
    }

    public async Task<ManagedAccount> ChangeStatusAsync(
        string callerUid,
        string targetUid,
        ChangeAccountStatusCommand command,
        CancellationToken cancellationToken)
    {
        ValidateTarget(callerUid, targetUid);
        var input = AccountInputValidator.Validate(command);
        var profile = await GetManageableProfileAsync(targetUid, cancellationToken)
            .ConfigureAwait(false);
        var identity = await GetIdentityAsync(targetUid, cancellationToken).ConfigureAwait(false);
        var desiredDisabled = input.Status == AccountStatus.Locked;

        if (identity.Disabled != desiredDisabled)
        {
            await store.SetIdentityDisabledAsync(
                targetUid,
                desiredDisabled,
                cancellationToken).ConfigureAwait(false);
        }

        if (profile.Status == input.Status)
        {
            return profile;
        }

        try
        {
            await store.UpdateProfileStatusAsync(
                targetUid,
                input.Status,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.SynchronizationFailure(
                "update-firestore-status",
                targetUid,
                exception);

            if (!desiredDisabled)
            {
                try
                {
                    await store.SetIdentityDisabledAsync(
                        targetUid,
                        disabled: true,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception rollbackException)
                    when (rollbackException is not OperationCanceledException)
                {
                    logger.SynchronizationFailure(
                        "disable-auth-after-unlock-failure",
                        targetUid,
                        rollbackException);
                }
            }

            throw new AccountSynchronizationException(
                "Authentication and Firestore status could not be synchronized. " +
                "The operation was left fail-closed and may be retried.");
        }

        return profile with { Status = input.Status };
    }

    private async Task<ManagedAccount> GetManageableProfileAsync(
        string uid,
        CancellationToken cancellationToken)
    {
        var profile = await store.FindProfileAsync(uid, cancellationToken).ConfigureAwait(false)
            ?? throw new AccountNotFoundException("Account profile was not found.");
        if (profile.Role is not (AccountRole.Employee or AccountRole.Manager))
        {
            throw new AccountProtectedException(
                "Admin accounts and unknown roles cannot be managed by F02 endpoints.");
        }

        if (!AccountStatus.IsValid(profile.Status))
        {
            throw new AccountConflictException("Account profile status is invalid.");
        }

        return profile;
    }

    private async Task<AccountIdentity> GetIdentityAsync(
        string uid,
        CancellationToken cancellationToken) =>
        await store.FindIdentityAsync(uid, cancellationToken).ConfigureAwait(false)
            ?? throw new AccountConflictException(
                "The Firestore profile has no matching Firebase Authentication account.");

    private async Task<bool> ProfileMatchesAfterFailureAsync(
        ManagedAccount expected,
        CancellationToken cancellationToken)
    {
        try
        {
            var saved = await store.FindProfileAsync(expected.Uid, cancellationToken)
                .ConfigureAwait(false);
            return saved == expected;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.SynchronizationFailure(
                "verify-profile-after-create-failure",
                expected.Uid,
                exception);
            return false;
        }
    }

    private async Task DisableAfterCreateFailureAsync(
        string uid,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        logger.SynchronizationFailure("create-firestore-profile", uid, originalException);
        try
        {
            await store.SetIdentityDisabledAsync(uid, disabled: true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.SynchronizationFailure("disable-auth-after-create-failure", uid, exception);
        }
    }

    private async Task TryRestoreDisplayNameAsync(
        string uid,
        string displayName,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        logger.SynchronizationFailure("update-firestore-profile", uid, originalException);
        try
        {
            await store.UpdateIdentityDisplayNameAsync(uid, displayName, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.SynchronizationFailure("restore-auth-display-name", uid, exception);
            throw new AccountSynchronizationException(
                "The profile update failed and the Authentication display name could not be restored.");
        }
    }

    private static void ValidateTarget(string callerUid, string targetUid)
    {
        ValidateCallerUid(callerUid);
        if (string.IsNullOrWhiteSpace(targetUid))
        {
            throw new AccountValidationException("Target UID is required.");
        }

        if (string.Equals(callerUid, targetUid, StringComparison.Ordinal))
        {
            throw new AccountProtectedException(
                "Administrators cannot modify or lock their own account through F02 endpoints.");
        }
    }

    private static void ValidateCallerUid(string callerUid)
    {
        if (string.IsNullOrWhiteSpace(callerUid))
        {
            throw new AccountValidationException("Verified caller UID is required.");
        }
    }
}

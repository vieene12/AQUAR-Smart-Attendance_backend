using SmartAttendance.Domain.Accounts;

namespace SmartAttendance.Application.Accounts;

public sealed record CreateAccountCommand(
    string FullName,
    string Email,
    string Password,
    string Role,
    long HourlyRate);

public sealed record UpdateAccountCommand(string FullName, long HourlyRate);

public sealed record ChangeAccountStatusCommand(string Status);

public sealed record AccountIdentity(
    string Uid,
    string Email,
    string? DisplayName,
    bool Disabled);

public sealed record AccountPage(
    IReadOnlyList<ManagedAccount> Items,
    string? NextPageToken);

public interface IAccountManagementService
{
    Task<AccountPage> ListAsync(int pageSize, string? pageToken, CancellationToken cancellationToken);

    Task<ManagedAccount> CreateAsync(
        string callerUid,
        CreateAccountCommand command,
        CancellationToken cancellationToken);

    Task<ManagedAccount> UpdateAsync(
        string callerUid,
        string targetUid,
        UpdateAccountCommand command,
        CancellationToken cancellationToken);

    Task<ManagedAccount> ChangeStatusAsync(
        string callerUid,
        string targetUid,
        ChangeAccountStatusCommand command,
        CancellationToken cancellationToken);
}

public interface IAccountStore
{
    Task<AccountPage> ListProfilesAsync(
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken);

    Task<ManagedAccount?> FindProfileAsync(string uid, CancellationToken cancellationToken);

    Task<AccountIdentity?> FindIdentityAsync(string uid, CancellationToken cancellationToken);

    Task<AccountIdentity> CreateIdentityAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken);

    Task CreateProfileAsync(ManagedAccount account, CancellationToken cancellationToken);

    Task UpdateIdentityDisplayNameAsync(
        string uid,
        string displayName,
        CancellationToken cancellationToken);

    Task SetIdentityDisabledAsync(
        string uid,
        bool disabled,
        CancellationToken cancellationToken);

    Task UpdateProfileAsync(
        string uid,
        string fullName,
        long hourlyRate,
        CancellationToken cancellationToken);

    Task UpdateProfileStatusAsync(
        string uid,
        string status,
        CancellationToken cancellationToken);
}

public interface IAccountOperationLogger
{
    void SynchronizationFailure(
        string operationStep,
        string targetUid,
        Exception exception);
}

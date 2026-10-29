using SmartAttendance.Application.Accounts;
using SmartAttendance.Domain.Accounts;

namespace SmartAttendance.UnitTests.Accounts;

internal sealed class FakeAccountStore : IAccountStore
{
    public List<string> Calls { get; } = [];

    public AccountPage Page { get; set; } = new([], null);

    public ManagedAccount? Profile { get; set; }

    public AccountIdentity? Identity { get; set; }

    public Exception? CreateProfileFailure { get; set; }

    public Exception? CreateIdentityFailure { get; set; }

    public Exception? UpdateIdentityFailure { get; set; }

    public Exception? UpdateProfileFailure { get; set; }

    public Exception? UpdateStatusFailure { get; set; }

    public Exception? SetDisabledFailure { get; set; }

    public Task<AccountPage> ListProfilesAsync(
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        Calls.Add($"list:{pageSize}:{pageToken}");
        return Task.FromResult(Page);
    }

    public Task<ManagedAccount?> FindProfileAsync(
        string uid,
        CancellationToken cancellationToken)
    {
        Calls.Add($"find-profile:{uid}");
        return Task.FromResult(Profile);
    }

    public Task<AccountIdentity?> FindIdentityAsync(
        string uid,
        CancellationToken cancellationToken)
    {
        Calls.Add($"find-auth:{uid}");
        return Task.FromResult(Identity);
    }

    public Task<AccountIdentity> CreateIdentityAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken)
    {
        Calls.Add("create-auth");
        if (CreateIdentityFailure is not null)
        {
            throw CreateIdentityFailure;
        }

        return Task.FromResult(
            Identity ?? new AccountIdentity("created-uid", email, displayName, false));
    }

    public Task CreateProfileAsync(
        ManagedAccount account,
        CancellationToken cancellationToken)
    {
        Calls.Add("create-profile");
        if (CreateProfileFailure is not null)
        {
            throw CreateProfileFailure;
        }

        Profile = account;
        return Task.CompletedTask;
    }

    public Task UpdateIdentityDisplayNameAsync(
        string uid,
        string displayName,
        CancellationToken cancellationToken)
    {
        Calls.Add($"auth-name:{displayName}");
        if (UpdateIdentityFailure is not null)
        {
            throw UpdateIdentityFailure;
        }

        Identity = Identity is null ? null : Identity with { DisplayName = displayName };
        return Task.CompletedTask;
    }

    public Task SetIdentityDisabledAsync(
        string uid,
        bool disabled,
        CancellationToken cancellationToken)
    {
        Calls.Add($"auth-disabled:{disabled}");
        if (SetDisabledFailure is not null)
        {
            throw SetDisabledFailure;
        }

        Identity = Identity is null ? null : Identity with { Disabled = disabled };
        return Task.CompletedTask;
    }

    public Task UpdateProfileAsync(
        string uid,
        string fullName,
        long hourlyRate,
        CancellationToken cancellationToken)
    {
        Calls.Add("update-profile");
        if (UpdateProfileFailure is not null)
        {
            throw UpdateProfileFailure;
        }

        Profile = Profile is null
            ? null
            : Profile with { FullName = fullName, HourlyRate = hourlyRate };
        return Task.CompletedTask;
    }

    public Task UpdateProfileStatusAsync(
        string uid,
        string status,
        CancellationToken cancellationToken)
    {
        Calls.Add($"profile-status:{status}");
        if (UpdateStatusFailure is not null)
        {
            throw UpdateStatusFailure;
        }

        Profile = Profile is null ? null : Profile with { Status = status };
        return Task.CompletedTask;
    }
}

internal sealed class FakeAccountOperationLogger : IAccountOperationLogger
{
    public List<(string Step, string Uid)> Failures { get; } = [];

    public void SynchronizationFailure(
        string operationStep,
        string targetUid,
        Exception exception) =>
        Failures.Add((operationStep, targetUid));
}

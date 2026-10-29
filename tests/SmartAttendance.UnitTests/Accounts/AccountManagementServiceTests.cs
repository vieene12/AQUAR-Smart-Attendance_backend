using SmartAttendance.Application.Accounts;
using SmartAttendance.Application.Authentication;
using SmartAttendance.Domain.Accounts;

namespace SmartAttendance.UnitTests.Accounts;

public sealed class AccountManagementServiceTests
{
    private const string AdminUid = "admin-uid";
    private const string EmployeeUid = "employee-uid";

    [Fact]
    public async Task CreateWritesAuthThenExactFirestoreSchema()
    {
        var (service, store, _) = CreateService();

        var created = await service.CreateAsync(
            AdminUid,
            ValidCreate(),
            CancellationToken.None);

        Assert.Equal(
            ["create-auth", "create-profile"],
            store.Calls);
        Assert.Equal(AccountStatus.Active, created.Status);
        Assert.Equal(AccountRole.Employee, created.Role);
        Assert.DoesNotContain("secret123", created.ToString());
    }

    [Fact]
    public async Task CreateDisablesAuthWhenFirestoreCannotBeConfirmed()
    {
        var (service, store, logger) = CreateService();
        store.CreateProfileFailure = new FirebaseServiceUnavailableException("write failed");

        await Assert.ThrowsAsync<AccountSynchronizationException>(() =>
            service.CreateAsync(
                AdminUid,
                ValidCreate(),
                CancellationToken.None));

        Assert.Contains("auth-disabled:True", store.Calls);
        Assert.DoesNotContain(store.Calls, call => call.Contains("delete", StringComparison.Ordinal));
        Assert.Contains(logger.Failures, item => item.Step == "create-firestore-profile");
    }

    [Fact]
    public async Task CreateAuthFailureNeverWritesFirestore()
    {
        var (service, store, _) = CreateService();
        store.CreateIdentityFailure = new FirebaseServiceUnavailableException("auth failed");

        await Assert.ThrowsAsync<FirebaseServiceUnavailableException>(() =>
            service.CreateAsync(AdminUid, ValidCreate(), CancellationToken.None));

        Assert.Equal(["create-auth"], store.Calls);
    }

    [Fact]
    public async Task CreateTreatsCommittedProfileAfterTransportErrorAsSuccess()
    {
        var (service, store, _) = CreateService();
        var expected = new ManagedAccount(
            "created-uid",
            "Employee A",
            "employee@example.com",
            AccountRole.Employee,
            AccountStatus.Active,
            50_000);
        store.CreateProfileFailure = new FirebaseServiceUnavailableException("timeout");
        store.Profile = expected;

        var created = await service.CreateAsync(
            AdminUid,
            ValidCreate(),
            CancellationToken.None);

        Assert.Equal(expected, created);
        Assert.DoesNotContain("auth-disabled:True", store.Calls);
    }

    [Fact]
    public async Task UpdateRejectsAdminTargetBeforeChangingIdentity()
    {
        var (service, store, _) = CreateService();
        store.Profile = Existing() with { Role = AccountRole.Admin };
        store.Identity = Identity();

        await Assert.ThrowsAsync<AccountProtectedException>(() =>
            service.UpdateAsync(
                AdminUid,
                EmployeeUid,
                new UpdateAccountCommand("New Name", 60_000),
                CancellationToken.None));

        Assert.DoesNotContain(store.Calls, call => call.StartsWith("auth-name", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateRestoresAuthDisplayNameWhenFirestoreFails()
    {
        var (service, store, logger) = CreateService();
        store.Profile = Existing();
        store.Identity = Identity();
        store.UpdateProfileFailure = new FirebaseServiceUnavailableException("write failed");

        await Assert.ThrowsAsync<FirebaseServiceUnavailableException>(() =>
            service.UpdateAsync(
                AdminUid,
                EmployeeUid,
                new UpdateAccountCommand("New Name", 60_000),
                CancellationToken.None));

        Assert.Equal(
            ["auth-name:New Name", "update-profile", "auth-name:Employee A"],
            store.Calls.Where(call => call.StartsWith("auth-name", StringComparison.Ordinal) ||
                call == "update-profile"));
        Assert.Contains(logger.Failures, item => item.Step == "update-firestore-profile");
    }

    [Fact]
    public async Task UpdateAuthFailureNeverWritesFirestore()
    {
        var (service, store, _) = CreateService();
        store.Profile = Existing();
        store.Identity = Identity();
        store.UpdateIdentityFailure = new FirebaseServiceUnavailableException("auth failed");

        await Assert.ThrowsAsync<FirebaseServiceUnavailableException>(() =>
            service.UpdateAsync(
                AdminUid,
                EmployeeUid,
                new UpdateAccountCommand("New Name", 60_000),
                CancellationToken.None));

        Assert.DoesNotContain("update-profile", store.Calls);
    }

    [Fact]
    public async Task UnlockFailureReturnsAuthToDisabledFailClosedState()
    {
        var (service, store, _) = CreateService();
        store.Profile = Existing() with { Status = AccountStatus.Locked };
        store.Identity = Identity() with { Disabled = true };
        store.UpdateStatusFailure = new FirebaseServiceUnavailableException("write failed");

        await Assert.ThrowsAsync<AccountSynchronizationException>(() =>
            service.ChangeStatusAsync(
                AdminUid,
                EmployeeUid,
                new ChangeAccountStatusCommand(AccountStatus.Active),
                CancellationToken.None));

        Assert.Equal(
            ["auth-disabled:False", "profile-status:active", "auth-disabled:True"],
            store.Calls.Where(call => call.StartsWith("auth-disabled", StringComparison.Ordinal) ||
                call.StartsWith("profile-status", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task StatusAuthFailureNeverChangesFirestore()
    {
        var (service, store, _) = CreateService();
        store.Profile = Existing();
        store.Identity = Identity();
        store.SetDisabledFailure = new FirebaseServiceUnavailableException("auth failed");

        await Assert.ThrowsAsync<FirebaseServiceUnavailableException>(() =>
            service.ChangeStatusAsync(
                AdminUid,
                EmployeeUid,
                new ChangeAccountStatusCommand(AccountStatus.Locked),
                CancellationToken.None));

        Assert.DoesNotContain(
            store.Calls,
            call => call.StartsWith("profile-status", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RepeatingDesiredStatusIsIdempotent()
    {
        var (service, store, _) = CreateService();
        store.Profile = Existing() with { Status = AccountStatus.Locked };
        store.Identity = Identity() with { Disabled = true };

        var result = await service.ChangeStatusAsync(
            AdminUid,
            EmployeeUid,
            new ChangeAccountStatusCommand(AccountStatus.Locked),
            CancellationToken.None);

        Assert.Equal(AccountStatus.Locked, result.Status);
        Assert.DoesNotContain(
            store.Calls,
            call => call.StartsWith("profile-status", StringComparison.Ordinal) ||
                call.StartsWith("auth-disabled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CannotModifyCallingAdminThroughF02()
    {
        var (service, _, _) = CreateService();

        await Assert.ThrowsAsync<AccountProtectedException>(() =>
            service.ChangeStatusAsync(
                AdminUid,
                AdminUid,
                new ChangeAccountStatusCommand(AccountStatus.Locked),
                CancellationToken.None));
    }

    private static (AccountManagementService Service, FakeAccountStore Store,
        FakeAccountOperationLogger Logger) CreateService()
    {
        var store = new FakeAccountStore();
        var logger = new FakeAccountOperationLogger();
        return (new AccountManagementService(store, logger), store, logger);
    }

    private static CreateAccountCommand ValidCreate() =>
        new("Employee A", "employee@example.com", "secret123", "employee", 50_000);

    private static ManagedAccount Existing() =>
        new(
            EmployeeUid,
            "Employee A",
            "employee@example.com",
            AccountRole.Employee,
            AccountStatus.Active,
            50_000);

    private static AccountIdentity Identity() =>
        new(EmployeeUid, "employee@example.com", "Employee A", false);
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAttendance.Application.Accounts;
using SmartAttendance.Application.Authentication;
using SmartAttendance.UnitTests.Authentication;

namespace SmartAttendance.UnitTests.Accounts;

internal sealed class AccountApiWebApplicationFactory(
    string role,
    FakeAccountStore store) : WebApplicationFactory<Program>
{
    public const string CallerUid = "caller-uid";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IFirebaseIdTokenVerifier>();
            services.RemoveAll<IUserProfileReader>();
            services.RemoveAll<IAccountStore>();
            services.RemoveAll<IAccountOperationLogger>();
            services.AddSingleton<IFirebaseIdTokenVerifier>(
                new FakeFirebaseIdTokenVerifier((_, _) => new FirebaseIdentity(CallerUid)));
            services.AddSingleton<IUserProfileReader>(
                new FakeUserProfileReader(new UserProfile(CallerUid, role, "active")));
            services.AddSingleton<IAccountStore>(store);
            services.AddSingleton<IAccountOperationLogger, FakeAccountOperationLogger>();
        });
    }
}

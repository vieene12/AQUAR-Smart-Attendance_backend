using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAttendance.Application.Authentication;

namespace SmartAttendance.UnitTests.Authentication;

internal sealed class FirebaseSecurityWebApplicationFactory(
    IFirebaseIdTokenVerifier tokenVerifier,
    IUserProfileReader profileReader)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IFirebaseIdTokenVerifier>();
            services.RemoveAll<IUserProfileReader>();
            services.AddSingleton(tokenVerifier);
            services.AddSingleton(profileReader);
        });
    }
}

using Microsoft.AspNetCore.Authorization;
using SmartAttendance.Api.Authorization;

namespace SmartAttendance.Api.Authentication;

public static class FirebaseAuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddFirebaseApiSecurity(this IServiceCollection services)
    {
        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme =
                    FirebaseAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme =
                    FirebaseAuthenticationDefaults.AuthenticationScheme;
            })
            .AddScheme<FirebaseAuthenticationOptions, FirebaseAuthenticationHandler>(
                FirebaseAuthenticationDefaults.AuthenticationScheme,
                configureOptions: null);

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.ActiveUser, policy =>
                policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(FirebaseClaimTypes.Status, FirebaseSecurityValues.ActiveStatus)
                    .RequireRole(
                        FirebaseSecurityValues.AdminRole,
                        FirebaseSecurityValues.ManagerRole,
                        FirebaseSecurityValues.EmployeeRole))
            .AddPolicy(AuthorizationPolicies.ActiveAdmin, policy =>
                policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(FirebaseClaimTypes.Status, FirebaseSecurityValues.ActiveStatus)
                    .RequireRole(FirebaseSecurityValues.AdminRole))
            .AddPolicy(AuthorizationPolicies.ManagerOrAdmin, policy =>
                policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(FirebaseClaimTypes.Status, FirebaseSecurityValues.ActiveStatus)
                    .RequireRole(
                        FirebaseSecurityValues.AdminRole,
                        FirebaseSecurityValues.ManagerRole))
            .AddPolicy(AuthorizationPolicies.EmployeeSelf, policy =>
                policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(FirebaseClaimTypes.Status, FirebaseSecurityValues.ActiveStatus)
                    .RequireRole(FirebaseSecurityValues.EmployeeRole)
                    .AddRequirements(new EmployeeSelfRequirement()));

        services.AddSingleton<IAuthorizationHandler, EmployeeSelfAuthorizationHandler>();
        return services;
    }
}

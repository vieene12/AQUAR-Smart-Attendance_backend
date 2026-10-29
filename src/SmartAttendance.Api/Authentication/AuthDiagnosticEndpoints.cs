using System.Security.Claims;
using SmartAttendance.Api.Authorization;

namespace SmartAttendance.Api.Authentication;

public static class AuthDiagnosticEndpoints
{
    public static IEndpointRouteBuilder MapAuthDiagnosticEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").WithTags("Authentication diagnostics");
        group.MapGet("/me", CreateResponse)
            .RequireAuthorization(AuthorizationPolicies.ActiveUser);
        group.MapGet("/admin-check", CreateResponse)
            .RequireAuthorization(AuthorizationPolicies.ActiveAdmin);
        return endpoints;
    }

    private static AuthenticatedCallerResponse CreateResponse(ClaimsPrincipal user) =>
        new(
            user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            user.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
            user.FindFirstValue(FirebaseClaimTypes.Status) ?? string.Empty);
}

public sealed record AuthenticatedCallerResponse(string Uid, string Role, string Status);

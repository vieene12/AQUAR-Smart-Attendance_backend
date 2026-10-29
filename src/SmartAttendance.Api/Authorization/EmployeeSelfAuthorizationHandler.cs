using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SmartAttendance.Api.Authentication;

namespace SmartAttendance.Api.Authorization;

public sealed class EmployeeSelfAuthorizationHandler
    : AuthorizationHandler<EmployeeSelfRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        EmployeeSelfRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext &&
            context.User.IsInRole(FirebaseSecurityValues.EmployeeRole) &&
            string.Equals(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                httpContext.Request.RouteValues["uid"]?.ToString(),
                StringComparison.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

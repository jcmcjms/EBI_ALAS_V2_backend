using EBI.ALAS.Api.Common.Extensions;
using Microsoft.AspNetCore.Authorization;

namespace EBI.ALAS.Api.Common.Authorization;

/// <summary>
/// Authorization handler that checks permissions from JWT claims.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User == null || !(context.User.Identity?.IsAuthenticated ?? false))
        {
            return Task.CompletedTask;
        }

        // Check if user has the required permission
        if (context.User!.HasPermission(requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

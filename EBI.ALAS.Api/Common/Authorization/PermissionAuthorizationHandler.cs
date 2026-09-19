using EBI.ALAS.Api.Common.Extensions;
using Microsoft.AspNetCore.Authorization;

namespace EBI.ALAS.Api.Common.Authorization;

/// <summary>
/// Authorization handler that checks for permission claims.
/// Succeeds if the authenticated user has the required permission.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        if (context.User.HasPermission(requirement.Permission))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

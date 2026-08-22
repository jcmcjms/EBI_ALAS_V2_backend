using System.Security.Claims;
using Alas.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;

namespace Alas.Api.Security;

public sealed class PermissionAuthorizationHandler: AuthorizationHandler<PermissionRequirement>
{
    private readonly IUserPermissionProvider _permissionProvider;

    public PermissionAuthorizationHandler(IUserPermissionProvider permissionProvider)
    {
        _permissionProvider = permissionProvider;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                          context.User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return;
        }

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        var permissionSet = await _permissionProvider.GetAsync(userId, cancellationToken);

        if (!permissionSet.IsActive)
        {
            return;
        }

        if (permissionSet.Permissions.Contains("*") ||
            permissionSet.Permissions.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
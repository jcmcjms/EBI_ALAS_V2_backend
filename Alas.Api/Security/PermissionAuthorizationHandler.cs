using System.Security.Claims;
using Alas.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Alas.Api.Security;

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IUserPermissionProvider _permissionProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PermissionAuthorizationHandler(
        IUserPermissionProvider permissionProvider,
        IHttpContextAccessor httpContextAccessor)
    {
        _permissionProvider = permissionProvider;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return;
        }

        var cancellationToken =
            _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;

        var permissionSet = await _permissionProvider.GetAsync(userId, cancellationToken);

        if (!permissionSet.IsActive)
        {
            return;
        }

        if (permissionSet.Permissions.Contains(AlasPermissions.SuperAdmin) ||
            permissionSet.Permissions.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}

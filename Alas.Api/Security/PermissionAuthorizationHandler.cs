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
        
       
        
    }
}
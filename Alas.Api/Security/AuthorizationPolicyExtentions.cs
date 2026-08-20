using Microsoft.AspNetCore.Authorization;

namespace Alas.Api.Security;

public static class AuthorizationPolicyExtentions
{
    public static AuthorizationPolicyBuilder RequirePermission(this AuthorizationPolicyBuilder builder,
        string permission)
    {
        return builder.AddRequirements(new PermissionRequirement(permission));
    }
}
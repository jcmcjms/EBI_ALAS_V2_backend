using Microsoft.AspNetCore.Authorization;

namespace EBI.ALAS.Api.Common.Authorization;
public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }
}

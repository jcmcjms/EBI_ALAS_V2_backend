using Microsoft.AspNetCore.Authorization;

namespace Alas.Api.Security;

public sealed class PermissionRequirement: IAuthorizationRequirement
{
    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}
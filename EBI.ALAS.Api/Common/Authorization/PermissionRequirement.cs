using Microsoft.AspNetCore.Authorization;

namespace EBI.ALAS.Api.Common.Authorization;

/// <summary>
/// Authorization requirement for permission-based access control.
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }
}

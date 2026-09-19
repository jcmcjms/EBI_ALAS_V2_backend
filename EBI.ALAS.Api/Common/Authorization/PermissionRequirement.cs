using Microsoft.AspNetCore.Authorization;

namespace EBI.ALAS.Api.Common.Authorization;

/// <summary>
/// Authorization requirement that checks for a specific permission claim.
/// Immutable — permission is set at construction time.
/// </summary>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

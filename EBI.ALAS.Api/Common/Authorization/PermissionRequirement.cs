using Microsoft.AspNetCore.Authorization;
namespace EBI.ALAS.Api.Common.Authorization;
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

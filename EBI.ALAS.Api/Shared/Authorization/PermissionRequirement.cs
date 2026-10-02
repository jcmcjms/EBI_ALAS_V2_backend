using Microsoft.AspNetCore.Authorization;
namespace EBI.ALAS.Api.Shared.Authorization;
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
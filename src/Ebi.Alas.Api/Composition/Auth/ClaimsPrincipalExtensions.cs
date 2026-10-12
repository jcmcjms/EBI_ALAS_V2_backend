using System.Security.Claims;

namespace Ebi.Alas.Api.Composition.Auth;

public static class ClaimsPrincipalExtensions
{
    public static string[] GetPermissions(this ClaimsPrincipal principal)
    {
        return principal.FindAll("permission")
            .Select(c => c.Value)
            .ToArray();
    }

    public static bool HasPermission(this ClaimsPrincipal principal, string permission)
    {
        var role = principal.FindFirst("role")?.Value
            ?? principal.FindFirst(ClaimTypes.Role)?.Value
            ?? string.Empty;
        if (role == "Admin")
            return true;
        return principal.GetPermissions().Contains(permission);
    }
}

using System.Security.Claims;

namespace EBI.ALAS.Api.Common.Extensions;

/// <summary>
/// Extension methods for extracting user information from JWT claims.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal principal)
    {
        var claim = principal.FindFirst("userId");
        return claim != null ? int.Parse(claim.Value) : 0;
    }

    public static string GetUsername(this ClaimsPrincipal principal)
    {
        return principal.FindFirst("username")?.Value ?? string.Empty;
    }

    public static string GetFirstName(this ClaimsPrincipal principal)
    {
        return principal.FindFirst("firstName")?.Value ?? string.Empty;
    }

    public static string? GetMiddleName(this ClaimsPrincipal principal)
    {
        return principal.FindFirst("middleName")?.Value;
    }

    public static string GetLastName(this ClaimsPrincipal principal)
    {
        return principal.FindFirst("lastName")?.Value ?? string.Empty;
    }

    public static string GetBranchId(this ClaimsPrincipal principal)
    {
        return principal.FindFirst("branchId")?.Value ?? string.Empty;
    }

    public static string GetRole(this ClaimsPrincipal principal)
    {
        return principal.FindFirst("role")?.Value ?? string.Empty;
    }

    public static string[] GetPermissions(this ClaimsPrincipal principal)
    {
        return principal.FindAll("permission")
            .Select(c => c.Value)
            .ToArray();
    }

    public static bool HasPermission(this ClaimsPrincipal principal, string permission)
    {
        var role = principal.GetRole();

        // Admin wildcard check
        if (role == "Admin")
            return true;

        return principal.GetPermissions().Contains(permission);
    }

    public static bool IsInRole(this ClaimsPrincipal principal, string role)
    {
        return principal.GetRole() == role;
    }
}

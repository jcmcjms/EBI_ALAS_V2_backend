using System.Security.Claims;

namespace EBI.ALAS.Api.Common.Extensions;
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

    /// <summary>
    /// Returns the acting officer's branch CODE (e.g. "011", "002") — the
    /// same value the JWT stores in the <c>branchId</c> claim. The token
    /// pipeline writes the user's <c>User.BranchId</c> directly into the
    /// claim (see <c>JwtTokenService</c>), and per the auth contract
    /// <c>User.BranchId == Branch.Code</c> — i.e. the claim value already
    /// IS the branch code, not the surrogate int id. So this helper is
    /// just a clearly-named alias for <see cref="GetBranchId"/> so the
    /// call sites in branch-scoped endpoints read correctly
    /// (`ctx.User.GetBranchCode()` instead of the misleading
    /// `ctx.User.GetBranchId()`).
    ///
    /// Returns an empty string when the claim is missing — endpoint code
    /// treats empty as "no branch scoping available" (the admin branch).
    /// </summary>
    public static string GetBranchCode(this ClaimsPrincipal principal)
    {
        return principal.GetBranchId();
    }

    public static string GetRole(this ClaimsPrincipal principal)
    {
        // .NET 8's JwtSecurityTokenHandler remaps inbound "role" claims to
        // ClaimTypes.Role by default. Look up both spellings so a token
        // minted with `new Claim("role", user.Role)` resolves regardless of
        // whether the runtime performed the remap.
        return principal.FindFirst("role")?.Value
            ?? principal.FindFirst(ClaimTypes.Role)?.Value
            ?? string.Empty;
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
        if (role == Common.Constants.Roles.Admin)
            return true;

        return principal.GetPermissions().Contains(permission);
    }

    public static bool IsInRole(this ClaimsPrincipal principal, string role)
    {
        return principal.GetRole() == role;
    }
}

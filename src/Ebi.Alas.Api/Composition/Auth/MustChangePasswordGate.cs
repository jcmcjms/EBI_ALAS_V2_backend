using System.Security.Claims;

namespace Ebi.Alas.Api.Composition.Auth;

/// <summary>
/// Server-side gate: callers with mustChangePassword may only reach
/// change-password / logout / anonymous auth and health endpoints.
/// </summary>
public static class MustChangePasswordGate
{
    private static readonly string[] AllowedPathPrefixes =
    [
        "/api/auth/change-password",
        "/api/auth/logout",
        "/api/auth/login",
        "/api/auth/refresh",
        "/health/",
    ];

    public static bool IsBlocked(ClaimsPrincipal user, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!RequiresPasswordChange(user))
        {
            return false;
        }

        foreach (var prefix in AllowedPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool RequiresPasswordChange(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var value = user.FindFirstValue("mustChangePassword");
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}

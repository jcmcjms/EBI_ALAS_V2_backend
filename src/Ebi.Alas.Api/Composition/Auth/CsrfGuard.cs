using System.Security.Claims;

namespace Ebi.Alas.Api.Composition.Auth;

/// <summary>
/// Double-submit CSRF check: unsafe methods on an authenticated request must
/// carry X-XSRF-TOKEN matching the access-token XsrfToken claim.
/// </summary>
public static class CsrfGuard
{
    public const string HeaderName = "X-XSRF-TOKEN";

    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "OPTIONS", "TRACE",
    };

    public static bool IsBlocked(string method, ClaimsPrincipal user, string? headerValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(user);

        if (SafeMethods.Contains(method))
        {
            return false;
        }

        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var claim = user.FindFirstValue("XsrfToken");
        if (string.IsNullOrWhiteSpace(claim))
        {
            return true;
        }

        return !string.Equals(claim, headerValue, StringComparison.Ordinal);
    }
}

using Microsoft.AspNetCore.Http;

namespace Ebi.Alas.Api.Features.Auth;

/// <summary>
/// HttpOnly cookie that carries the opaque refresh token so browsers can restore
/// sessions without putting the secret in JavaScript-accessible storage.
/// </summary>
public static class RefreshCookie
{
    public const string Name = "alas_refresh";

    public static void Append(HttpContext httpContext, string rawToken, DateTimeOffset expires)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);

        httpContext.Response.Cookies.Append(Name, rawToken, new CookieOptions
        {
            HttpOnly = true,
            // Always mark Secure: TLS is required for this credential, and
            // Request.IsHttps is false behind TLS-terminating proxies.
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth",
            Expires = expires,
            IsEssential = true,
        });
    }

    public static void Delete(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        httpContext.Response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth",
        });
    }

    public static string? Read(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Request.Cookies[Name];
    }
}

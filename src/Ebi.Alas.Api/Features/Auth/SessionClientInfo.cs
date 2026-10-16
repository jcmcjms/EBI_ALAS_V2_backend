namespace Ebi.Alas.Api.Features.Auth;

public sealed record SessionClientInfo(string? IpAddress, string? UserAgent, string? DeviceInfo)
{
    public static SessionClientInfo From(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        var ua = httpContext.Request.Headers.UserAgent.ToString();
        return Parse(ip, string.IsNullOrWhiteSpace(ua) ? null : ua);
    }

    public static SessionClientInfo Parse(string? ip, string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return new SessionClientInfo(TruncateIp(ip), null, null);
        }

        return new SessionClientInfo(TruncateIp(ip), TruncateUa(userAgent), Describe(userAgent));
    }

    private static string? TruncateIp(string? ip) =>
        string.IsNullOrWhiteSpace(ip) ? null : ip.Length <= 64 ? ip : ip[..64];

    private static string? TruncateUa(string? ua) =>
        string.IsNullOrWhiteSpace(ua) ? null : ua.Length <= 500 ? ua : ua[..500];

    private static string? Describe(string userAgent)
    {
        var browser =
            userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge" :
            userAgent.Contains("OPR/", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("Opera", StringComparison.OrdinalIgnoreCase) ? "Opera" :
            userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("CriOS", StringComparison.OrdinalIgnoreCase) ? "Chrome" :
            userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari" :
            userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("FxiOS", StringComparison.OrdinalIgnoreCase) ? "Firefox" :
            null;

        var os =
            userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows" :
            userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("iOS", StringComparison.OrdinalIgnoreCase) ? "iOS" :
            userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android" :
            userAgent.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) ? "macOS" :
            userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux" :
            null;

        return (browser, os) switch
        {
            (not null, not null) => $"{browser} on {os}",
            (not null, null) => browser,
            (null, not null) => os,
            _ => null,
        };
    }
}

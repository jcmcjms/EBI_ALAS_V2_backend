using System.Text.RegularExpressions;
namespace EBI.ALAS.Api.Features.Account;
public static class UserAgentParser
{
    public static string Describe(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "Unknown Device";
        var browser = DetectBrowser(userAgent);
        var os = DetectOs(userAgent);
        if (browser != null && os != null) return $"{browser} on {os}";
        if (browser != null) return browser;
        if (os != null) return os;
        return userAgent.Length > 100 ? userAgent[..100] + "…" : userAgent;
    }
    private static string? DetectBrowser(string ua)
    {
        if (Regex.IsMatch(ua, @"Edg[eA]?/\d", RegexOptions.IgnoreCase))
            return "Edge";
        if (Regex.IsMatch(ua, @"OPR/\d|Opera/\d", RegexOptions.IgnoreCase))
            return "Opera";
        if (Regex.IsMatch(ua, @"SamsungBrowser/\d", RegexOptions.IgnoreCase))
            return "Samsung Internet";
        if (Regex.IsMatch(ua, @"Chrome/\d", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(ua, @"Chromium/\d", RegexOptions.IgnoreCase))
            return "Chrome";
        if (Regex.IsMatch(ua, @"Chromium/\d", RegexOptions.IgnoreCase))
            return "Chromium";
        if (Regex.IsMatch(ua, @"Firefox/\d", RegexOptions.IgnoreCase))
            return "Firefox";
        if (Regex.IsMatch(ua, @"FxiOS/\d", RegexOptions.IgnoreCase))
            return "Firefox";
        if (Regex.IsMatch(ua, @"Version/\d", RegexOptions.IgnoreCase)
            && Regex.IsMatch(ua, @"Safari/\d", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(ua, @"Chrome/\d", RegexOptions.IgnoreCase))
            return "Safari";
        if (Regex.IsMatch(ua, @"MSIE \d|Trident/\d", RegexOptions.IgnoreCase))
            return "Internet Explorer";
        if (Regex.IsMatch(ua, @"Android", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(ua, @"Chrome/\d|Firefox/\d", RegexOptions.IgnoreCase))
            return "Android Browser";
        return null;
    }
    private static string? DetectOs(string ua)
    {
        if (Regex.IsMatch(ua, @"iPhone", RegexOptions.IgnoreCase)) return "iOS";
        if (Regex.IsMatch(ua, @"iPad", RegexOptions.IgnoreCase)) return "iPadOS";
        if (Regex.IsMatch(ua, @"CPU OS \d", RegexOptions.IgnoreCase)) return "iOS";
        if (Regex.IsMatch(ua, @"Mac OS X|macOS", RegexOptions.IgnoreCase))
        {
            if (Regex.IsMatch(ua, @"Mobile/\w+", RegexOptions.IgnoreCase)
                && !Regex.IsMatch(ua, @"Android", RegexOptions.IgnoreCase))
                return "iPadOS";
            return "macOS";
        }
        if (Regex.IsMatch(ua, @"Android", RegexOptions.IgnoreCase)) return "Android";
        if (Regex.IsMatch(ua, @"Windows NT \d", RegexOptions.IgnoreCase)) return "Windows";
        if (Regex.IsMatch(ua, @"Linux|X11", RegexOptions.IgnoreCase)) return "Linux";
        if (Regex.IsMatch(ua, @"CrOS", RegexOptions.IgnoreCase)) return "Chrome OS";
        return null;
    }
}

using System.Text.RegularExpressions;

namespace EBI.ALAS.Api.Features.Account;

/// <summary>
/// Best-effort User-Agent parser for the "Active Sessions" UI. Turns the raw
/// UA string browsers send into a short, human-readable description like
/// "Chrome on Windows" or "Safari on iOS".
///
/// Implementation is regex-based and intentionally simple — we are not
/// building a fingerprinting engine. The goal is just to avoid showing users
/// "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like
/// Gecko) Chrome/151.0.0.0 Safari/537.36" on the account page.
/// </summary>
public static class UserAgentParser
{
    /// <summary>
    /// Parse a User-Agent header into a short browser + OS label.
    /// Falls back to the raw string (or "Unknown Device" when null/empty)
    /// if nothing matches — better than showing an empty row.
    /// </summary>
    public static string Describe(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "Unknown Device";

        // Order matters: identify specific browsers before generic WebKit/AppleWebKit.
        var browser = DetectBrowser(userAgent);
        var os = DetectOs(userAgent);

        if (browser != null && os != null) return $"{browser} on {os}";
        if (browser != null) return browser;
        if (os != null) return os;

        // No recognizable signal — return the raw UA so the user can still
        // distinguish sessions (e.g. two CLI tokens with no UA match will
        // both look identical otherwise).
        return userAgent.Length > 100 ? userAgent[..100] + "…" : userAgent;
    }

    private static string? DetectBrowser(string ua)
    {
        // Edge (Chromium-based) — must be checked before Chrome because Edge
        // also contains "Chrome/" in its UA.
        if (Regex.IsMatch(ua, @"Edg[eA]?/\d", RegexOptions.IgnoreCase))
            return "Edge";

        // Opera (Chromium-based) — must be checked before Chrome for the same reason.
        if (Regex.IsMatch(ua, @"OPR/\d|Opera/\d", RegexOptions.IgnoreCase))
            return "Opera";

        // Samsung Internet — also Chromium-based, contains "Chrome/" + "SamsungBrowser".
        if (Regex.IsMatch(ua, @"SamsungBrowser/\d", RegexOptions.IgnoreCase))
            return "Samsung Internet";

        // Chrome — exclude Edge/Opera/Samsung by checking for "Chrome/" without
        // their specific markers above. Use word-boundary-ish checks.
        if (Regex.IsMatch(ua, @"Chrome/\d", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(ua, @"Chromium/\d", RegexOptions.IgnoreCase))
            return "Chrome";

        // Chromium (open-source build, no Chrome branding).
        if (Regex.IsMatch(ua, @"Chromium/\d", RegexOptions.IgnoreCase))
            return "Chromium";

        // Firefox — note that iOS Firefox uses "FxiOS", not "Firefox".
        if (Regex.IsMatch(ua, @"Firefox/\d", RegexOptions.IgnoreCase))
            return "Firefox";
        if (Regex.IsMatch(ua, @"FxiOS/\d", RegexOptions.IgnoreCase))
            return "Firefox";

        // Safari — must be checked AFTER Chrome because Chrome's UA also contains
        // "Safari/". The "Version/" token is Safari-specific.
        if (Regex.IsMatch(ua, @"Version/\d", RegexOptions.IgnoreCase)
            && Regex.IsMatch(ua, @"Safari/\d", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(ua, @"Chrome/\d", RegexOptions.IgnoreCase))
            return "Safari";

        // Internet Explorer / legacy Edge.
        if (Regex.IsMatch(ua, @"MSIE \d|Trident/\d", RegexOptions.IgnoreCase))
            return "Internet Explorer";

        // Generic mobile WebView (Android with no Chrome/Version markers).
        if (Regex.IsMatch(ua, @"Android", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(ua, @"Chrome/\d|Firefox/\d", RegexOptions.IgnoreCase))
            return "Android Browser";

        return null;
    }

    private static string? DetectOs(string ua)
    {
        // iOS — both iPhone and iPad. New iPadOS reports as Mac with touch.
        if (Regex.IsMatch(ua, @"iPhone", RegexOptions.IgnoreCase)) return "iOS";
        if (Regex.IsMatch(ua, @"iPad", RegexOptions.IgnoreCase)) return "iPadOS";
        if (Regex.IsMatch(ua, @"CPU OS \d", RegexOptions.IgnoreCase)) return "iOS";

        // macOS — including iPadOS-13-onwards that masquerades as Mac.
        if (Regex.IsMatch(ua, @"Mac OS X|macOS", RegexOptions.IgnoreCase))
        {
            // iPadOS reports "Macintosh; Intel Mac OS X" with "Mobile/" elsewhere;
            // treat it as iPadOS when we see touch + Mac tokens.
            if (Regex.IsMatch(ua, @"Mobile/\w+", RegexOptions.IgnoreCase)
                && !Regex.IsMatch(ua, @"Android", RegexOptions.IgnoreCase))
                return "iPadOS";
            return "macOS";
        }

        // Android — both phones and tablets share this token.
        if (Regex.IsMatch(ua, @"Android", RegexOptions.IgnoreCase)) return "Android";

        // Windows — NT version tokens (Win64; x64, etc.).
        if (Regex.IsMatch(ua, @"Windows NT \d", RegexOptions.IgnoreCase)) return "Windows";

        // Linux — generic. Be careful: Android already matched above, so this
        // is desktop distros + WSL etc.
        if (Regex.IsMatch(ua, @"Linux|X11", RegexOptions.IgnoreCase)) return "Linux";

        // Chrome OS.
        if (Regex.IsMatch(ua, @"CrOS", RegexOptions.IgnoreCase)) return "Chrome OS";

        return null;
    }
}
namespace Ebi.Alas.Api.Features.Users.Domain;

/// <summary>
/// Validation policy for stored e-signature ink (base64 data URLs).
/// Centralised so profile upload, signature-chain snapshots and display
/// mapping all enforce identical rules. SVG/HTML ink is rejected.
/// </summary>
public static class ESignaturePolicy
{
    /// <summary>~1.5 MB of PNG as base64; matches the canvas pad output.</summary>
    public const int MaxLength = 2_000_000;

    private static readonly string[] AllowedMimeTypes =
        ["image/png", "image/jpeg", "image/webp"];

    public static bool IsValid(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl))
        {
            return false;
        }

        var trimmed = dataUrl.Trim();
        if (trimmed.Length > MaxLength
            || !trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separator = trimmed.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
        if (separator < 0)
        {
            return false;
        }

        var mime = trimmed["data:".Length..separator];
        return AllowedMimeTypes.Contains(mime, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Returns the ink when it satisfies the policy; otherwise null. Safe for read paths.</summary>
    public static string? Sanitize(string? dataUrl) =>
        IsValid(dataUrl) ? dataUrl!.Trim() : null;
}

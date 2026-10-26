namespace Ebi.Alas.Api.Features.Users.Domain;

/// <summary>
/// Validation policy for stored e-signature ink (base64 data URLs).
/// Upload is PNG-only after decode/sanitize. Display accepts legacy
/// PNG/JPEG/WEBP data URLs so historical snapshots remain readable.
/// </summary>
public static class ESignaturePolicy
{
    /// <summary>~2 MB of PNG as base64; matches the upload size cap.</summary>
    public const int MaxLength = 2_000_000;

    private static readonly string[] AllowedDisplayMimeTypes =
        ["image/png", "image/jpeg", "image/webp"];

    /// <summary>Strict shape check for new uploads (PNG label only). Content is validated by the sanitizer.</summary>
    public static bool IsValidUpload(string? dataUrl)
    {
        if (!TryParse(dataUrl, out var trimmed, out var mime))
        {
            return false;
        }

        return mime.Equals("image/png", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Decodes, validates, and re-encodes a PNG upload. Returns the sanitized data URL, or null.
    /// </summary>
    public static string? SanitizeUpload(string? dataUrl)
    {
        if (!IsValidUpload(dataUrl)
            || !PngSignatureSanitizer.TryDecodeBase64Payload(dataUrl!.Trim(), out var payload))
        {
            return null;
        }

        return PngSignatureSanitizer.TrySanitizeToDataUrl(payload);
    }

    /// <summary>
    /// Display-path filter: accepts legacy PNG/JPEG/WEBP data URLs without re-encoding.
    /// Never use this for new writes.
    /// </summary>
    public static string? SanitizeForDisplay(string? dataUrl)
    {
        if (!TryParse(dataUrl, out var trimmed, out var mime))
        {
            return null;
        }

        return AllowedDisplayMimeTypes.Contains(mime, StringComparer.OrdinalIgnoreCase)
            ? trimmed
            : null;
    }

    /// <summary>True when ink can be snapshotted onto a new signed decision (PNG only).</summary>
    public static bool IsEligibleForSigning(string? dataUrl) =>
        SanitizeForDisplay(dataUrl) is { } ink
        && ink.StartsWith("data:image/png;", StringComparison.OrdinalIgnoreCase);

    /// <summary>Backward-compatible name used by snapshot/display mapping.</summary>
    public static string? Sanitize(string? dataUrl) => SanitizeForDisplay(dataUrl);

    private static bool TryParse(string? dataUrl, out string trimmed, out string mime)
    {
        trimmed = string.Empty;
        mime = string.Empty;

        if (string.IsNullOrWhiteSpace(dataUrl))
        {
            return false;
        }

        trimmed = dataUrl.Trim();
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

        mime = trimmed["data:".Length..separator];
        return mime.Length > 0;
    }
}

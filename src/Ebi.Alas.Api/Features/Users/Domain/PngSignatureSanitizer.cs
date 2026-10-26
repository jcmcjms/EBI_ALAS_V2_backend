using SkiaSharp;

namespace Ebi.Alas.Api.Features.Users.Domain;

/// <summary>
/// Decodes, validates, and re-encodes uploaded signature PNGs so only
/// sanitized, metadata-free images reach persistence.
/// </summary>
public static class PngSignatureSanitizer
{
    public const int MaxEncodedBytes = 2 * 1024 * 1024;
    public const int MaxWidth = 4096;
    public const int MaxHeight = 4096;
    public const int MinWidth = 32;
    public const int MinHeight = 16;
    public const long MaxDecodedPixels = 16_000_000;

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Validates and sanitizes a PNG payload. Returns a data URL of the re-encoded image,
    /// or null when the payload is not an acceptable signature PNG.
    /// </summary>
    public static string? TrySanitizeToDataUrl(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0 || payload.Length > MaxEncodedBytes)
        {
            return null;
        }

        if (!HasPngMagic(payload))
        {
            return null;
        }

        try
        {
            var bytes = payload.ToArray();

            // Identify first so decompression bombs fail before allocating a full raster.
            using var codec = SKCodec.Create(new SKMemoryStream(bytes));
            if (codec is null)
            {
                return null;
            }

            var info = codec.Info;
            if (info.Width < MinWidth
                || info.Height < MinHeight
                || info.Width > MaxWidth
                || info.Height > MaxHeight
                || (long)info.Width * info.Height > MaxDecodedPixels)
            {
                return null;
            }

            if (codec.EncodedFormat != SKEncodedImageFormat.Png)
            {
                return null;
            }

            using var bitmap = SKBitmap.Decode(codec);
            if (bitmap is null)
            {
                return null;
            }

            // Metadata is dropped by re-encoding a fresh PNG from the raster.
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            if (encoded is null)
            {
                return null;
            }

            var sanitized = encoded.ToArray();
            if (sanitized.Length == 0 || sanitized.Length > MaxEncodedBytes)
            {
                return null;
            }

            return "data:image/png;base64," + Convert.ToBase64String(sanitized);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException or IOException)
        {
            return null;
        }
    }

    /// <summary>Extracts decoded bytes from a base64 data URL payload without trusting the MIME label alone.</summary>
    public static bool TryDecodeBase64Payload(string dataUrl, out byte[] payload)
    {
        payload = [];
        var separator = dataUrl.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
        if (separator < 0)
        {
            return false;
        }

        var base64 = dataUrl[(separator + ";base64,".Length)..].Trim();
        if (base64.Length == 0 || base64.Length > (MaxEncodedBytes * 4 / 3) + 4)
        {
            return false;
        }

        try
        {
            payload = Convert.FromBase64String(base64);
            return payload.Length > 0 && payload.Length <= MaxEncodedBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool HasPngMagic(ReadOnlySpan<byte> payload) =>
        payload.Length >= PngMagic.Length
        && payload[..PngMagic.Length].SequenceEqual(PngMagic);
}

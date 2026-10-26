using Ebi.Alas.Api.Features.Users.Domain;
using SkiaSharp;

namespace Ebi.Alas.Api.Tests.Features.Users;

public sealed class PngSignatureSanitizerTests
{
    private static byte[] CreatePng(int width, int height, byte alpha = 255) =>
        SignatureTestImages.CreatePng(width, height, alpha);

    private static string ToDataUrl(byte[] bytes) => SignatureTestImages.ToDataUrl(bytes);

    [Fact]
    public void TrySanitizeToDataUrl_accepts_valid_png_and_returns_data_url()
    {
        var sanitized = PngSignatureSanitizer.TrySanitizeToDataUrl(CreatePng(200, 80));

        Assert.NotNull(sanitized);
        Assert.StartsWith("data:image/png;base64,", sanitized);
        Assert.True(PngSignatureSanitizer.TryDecodeBase64Payload(sanitized!, out var outBytes));
        Assert.True(PngSignatureSanitizer.HasPngMagic(outBytes));
    }

    [Fact]
    public void TrySanitizeToDataUrl_preserves_transparency()
    {
        var sanitized = PngSignatureSanitizer.TrySanitizeToDataUrl(CreatePng(120, 40, alpha: 128));
        Assert.NotNull(sanitized);

        Assert.True(PngSignatureSanitizer.TryDecodeBase64Payload(sanitized!, out var bytes));
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        Assert.Equal(128, bitmap.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void TrySanitizeToDataUrl_rejects_non_png_magic()
    {
        var jpegish = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        Assert.Null(PngSignatureSanitizer.TrySanitizeToDataUrl(jpegish));
    }

    [Fact]
    public void TrySanitizeToDataUrl_rejects_truncated_png()
    {
        var png = CreatePng(64, 32);
        var truncated = png.AsSpan(0, Math.Max(8, png.Length / 3)).ToArray();
        Assert.Null(PngSignatureSanitizer.TrySanitizeToDataUrl(truncated));
    }

    [Fact]
    public void TrySanitizeToDataUrl_rejects_malformed_payload()
    {
        var bogus = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01, 0x02 };
        Assert.Null(PngSignatureSanitizer.TrySanitizeToDataUrl(bogus));
    }

    [Fact]
    public void TrySanitizeToDataUrl_rejects_oversized_encoded_payload()
    {
        var oversized = new byte[PngSignatureSanitizer.MaxEncodedBytes + 1];
        Assert.Null(PngSignatureSanitizer.TrySanitizeToDataUrl(oversized));
    }

    [Fact]
    public void TrySanitizeToDataUrl_rejects_too_small_dimensions()
    {
        Assert.Null(PngSignatureSanitizer.TrySanitizeToDataUrl(CreatePng(8, 8)));
    }

    [Fact]
    public void TrySanitizeToDataUrl_rejects_excessive_dimensions()
    {
        // Identify rejects before full raster allocation for oversized frames.
        var huge = CreatePng(PngSignatureSanitizer.MaxWidth + 1, PngSignatureSanitizer.MinHeight);
        Assert.Null(PngSignatureSanitizer.TrySanitizeToDataUrl(huge));
    }

    [Fact]
    public void SanitizeUpload_returns_sanitized_png_data_url()
    {
        var dataUrl = ToDataUrl(CreatePng(100, 50));
        var result = ESignaturePolicy.SanitizeUpload(dataUrl);
        Assert.NotNull(result);
        Assert.StartsWith("data:image/png;base64,", result);
        Assert.True(ESignaturePolicy.IsEligibleForSigning(result));
    }

    [Fact]
    public void SanitizeUpload_rejects_jpeg_and_webp_labels()
    {
        Assert.Null(ESignaturePolicy.SanitizeUpload("data:image/jpeg;base64,/9j/4AAQ="));
        Assert.Null(ESignaturePolicy.SanitizeUpload("data:image/webp;base64,UklGRg=="));
    }

    [Fact]
    public void SanitizeUpload_rejects_png_label_with_non_image_payload()
    {
        Assert.Null(ESignaturePolicy.SanitizeUpload("data:image/png;base64,SGVsbG8gV29ybGQ="));
    }
}

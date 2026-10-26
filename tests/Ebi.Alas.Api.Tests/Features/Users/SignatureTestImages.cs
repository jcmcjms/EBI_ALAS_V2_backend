using SkiaSharp;

namespace Ebi.Alas.Api.Tests.Features.Users;

internal static class SignatureTestImages
{
    public static byte[] CreatePng(int width = 200, int height = 80, byte alpha = 255)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(20, 20, 20, alpha));
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static string ToDataUrl(byte[] bytes) =>
        "data:image/png;base64," + Convert.ToBase64String(bytes);

    public static string CreatePngDataUrl(int width = 200, int height = 80) =>
        ToDataUrl(CreatePng(width, height));
}

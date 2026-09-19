using Microsoft.AspNetCore.ResponseCompression;

namespace EBI.ALAS.Api.Common.Extensions;

/// <summary>
/// HTTP response compression configuration (Brotli + Gzip).
/// Extracted from Program.cs to follow Single Responsibility Principle.
/// </summary>
public static class CompressionExtensions
{
    /// <summary>
    /// Configures Brotli + Gzip compression for HTTP responses.
    /// Reduces average JSON payload from ~200KB to ~30KB on the wire.
    /// MUST be registered before UseCors in the middleware pipeline.
    /// </summary>
    public static IServiceCollection AddBankingCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });

        services.Configure<BrotliCompressionProviderOptions>(options =>
        {
            options.Level = System.IO.Compression.CompressionLevel.Fastest;
        });

        services.Configure<GzipCompressionProviderOptions>(options =>
        {
            options.Level = System.IO.Compression.CompressionLevel.Fastest;
        });

        return services;
    }
}

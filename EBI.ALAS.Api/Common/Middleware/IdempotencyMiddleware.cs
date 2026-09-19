using System.Text.Json;
using System.Text.Json.Serialization;
using EBI.ALAS.Api.Common.Models;
using Microsoft.Extensions.Caching.Memory;

namespace EBI.ALAS.Api.Common.Middleware;

/// <summary>
/// Idempotency middleware backed by IMemoryCache.
/// Replays cached responses for POST/PUT/PATCH requests with an Idempotency-Key header.
/// Uses primary constructor for dependency injection.
/// </summary>
public sealed class IdempotencyMiddleware(
    RequestDelegate next,
    IMemoryCache cache,
    ILogger<IdempotencyMiddleware> logger)
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string CacheKeyPrefix = "idem:";
    private const int MaxCacheableBodyBytes = 1 * 1024 * 1024;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(90);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly HashSet<string> AlwaysStrippedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Set-Cookie",
        "Authorization",
        "X-XSRF-TOKEN"
    };

    private const string StrippedHeaderPrefix = "X-";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) &&
            !HttpMethods.IsPut(context.Request.Method) &&
            !HttpMethods.IsPatch(context.Request.Method))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var idempotencyKey) ||
            string.IsNullOrWhiteSpace(idempotencyKey))
        {
            await next(context);
            return;
        }

        var key = idempotencyKey.ToString();

        if (key.Length is < 8 or > 128)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                ApiResponse.ErrorResponse("Idempotency-Key must be between 8 and 128 characters"));
            return;
        }

        var cacheKey = CacheKeyPrefix + key;

        if (cache.TryGetValue(cacheKey, out CachedIdempotentResponse? cached) && cached is not null)
        {
            logger.LogInformation("Idempotency hit for key: {Key}", key);
            await WriteReplayAsync(context, cached);
            return;
        }

        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBodyStream;

            if (context.Response.StatusCode is >= 200 and < 300)
            {
                if (responseBody.Length > MaxCacheableBodyBytes)
                {
                    logger.LogWarning(
                        "Idempotency response body too large to cache ({Bytes} bytes > {Max} bytes); key={Key}",
                        responseBody.Length, MaxCacheableBodyBytes, key);
                }
                else
                {
                    var bodyBytes = responseBody.ToArray();
                    var safeHeaders = ExtractReplayableHeaders(context.Response.Headers);

                    var entry = new CachedIdempotentResponse
                    {
                        StatusCode = context.Response.StatusCode,
                        Body = bodyBytes,
                        Headers = safeHeaders
                    };

                    cache.Set(cacheKey, entry, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = CacheDuration,
                        Size = Math.Max(1, bodyBytes.Length / 1024)
                    });

                    logger.LogDebug("Cached idempotent response for key: {Key} ({Bytes} bytes)", key, bodyBytes.Length);
                }
            }

            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);
        }
    }

    private static Dictionary<string, string> ExtractReplayableHeaders(IHeaderDictionary responseHeaders)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in responseHeaders)
        {
            if (AlwaysStrippedHeaders.Contains(header.Key)) continue;
            if (header.Key.StartsWith(StrippedHeaderPrefix, StringComparison.OrdinalIgnoreCase)) continue;

            var joined = header.Value.ToString();
            if (!string.IsNullOrEmpty(joined))
                result[header.Key] = joined;
        }
        return result;
    }

    private static async Task WriteReplayAsync(HttpContext context, CachedIdempotentResponse cached)
    {
        context.Response.StatusCode = cached.StatusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        foreach (var header in cached.Headers)
        {
            if (AlwaysStrippedHeaders.Contains(header.Key)) continue;
            if (header.Key.StartsWith(StrippedHeaderPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            context.Response.Headers.Append(header.Key, header.Value);
        }

        await context.Response.Body.WriteAsync(cached.Body);
    }

    private sealed class CachedIdempotentResponse
    {
        public int StatusCode { get; init; }
        public byte[] Body { get; init; } = [];
        public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public static class IdempotencyMiddlewareExtensions
{
    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder builder)
        => builder.UseMiddleware<IdempotencyMiddleware>();
}

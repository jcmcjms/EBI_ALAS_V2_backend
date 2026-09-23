using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using EBI.ALAS.Api.Common.Models;
using Microsoft.Extensions.Caching.Memory;

namespace EBI.ALAS.Api.Common.Middleware;

/// <summary>
/// Idempotency middleware backed by IMemoryCache.
/// Replays cached responses for POST/PUT/PATCH requests with an Idempotency-Key header.
///
/// Cache key is now scoped by userId + method + path + client-supplied key.
/// This prevents one user from replaying another user's cached response (IDOR via idempotency).
/// Middleware must be registered AFTER UseAuthentication() so HttpContext.User is populated.
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

        // Scope the cache key by user identity, HTTP method, path, and the client key.
        // This prevents cross-user replay (one user's cached response served to another)
        // and prevents the same key from colliding across different endpoints.
        var userId = context.User?.FindFirst("userId")?.Value ?? "anon";
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? "/";
        var cacheKey = $"{CacheKeyPrefix}{userId}:{method}:{path}:{key}";

        if (cache.TryGetValue(cacheKey, out CachedIdempotentResponse? cached) && cached is not null)
        {
            // Verify the replaying user matches the original user.
            // Reject if a different user tries to use someone else's idempotency key.
            if (cached.UserId != userId)
            {
                context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                await context.Response.WriteAsJsonAsync(
                    ApiResponse.ErrorResponse("Idempotency-Key already claimed by another user."));
                return;
            }

            logger.LogInformation("Idempotency hit for key: {Key} (user: {UserId})", key, userId);
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
                        Headers = safeHeaders,
                        UserId = userId
                    };

                    cache.Set(cacheKey, entry, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = CacheDuration,
                        Size = Math.Max(1, bodyBytes.Length / 1024)
                    });

                    logger.LogDebug("Cached idempotent response for key: {Key} (user: {UserId}, {Bytes} bytes)",
                        key, userId, bodyBytes.Length);
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

        /// <summary>
        /// Track which user created this cached entry so we can reject
        /// replays from different users.
        /// </summary>
        public string UserId { get; init; } = "anon";
    }
}

public static class IdempotencyMiddlewareExtensions
{
    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder builder)
        => builder.UseMiddleware<IdempotencyMiddleware>();
}

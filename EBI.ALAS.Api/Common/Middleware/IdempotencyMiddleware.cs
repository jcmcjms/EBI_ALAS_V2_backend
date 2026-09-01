using System.Collections.Concurrent;
using System.Text.Json;
using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Common.Middleware;
public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IdempotencyMiddleware> _logger;
    private readonly ConcurrentDictionary<string, CachedResponse> _cache = new();

    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only apply to mutating methods
        if (!HttpMethods.IsPost(context.Request.Method) &&
            !HttpMethods.IsPut(context.Request.Method) &&
            !HttpMethods.IsPatch(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Check for idempotency key
        if (!context.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var idempotencyKey) ||
            string.IsNullOrWhiteSpace(idempotencyKey))
        {
            await _next(context);
            return;
        }

        var key = idempotencyKey.ToString();

        // Validate key format (reasonable length)
        if (key.Length < 8 || key.Length > 128)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                ApiResponse.ErrorResponse("Idempotency-Key must be between 8 and 128 characters"));
            return;
        }

        // Check cache
        if (_cache.TryGetValue(key, out var cached))
        {
            if (DateTime.UtcNow - cached.CreatedAt < CacheDuration)
            {
                _logger.LogInformation("Idempotency hit for key: {Key}", key);
                context.Response.StatusCode = cached.StatusCode;
                context.Response.ContentType = "application/json; charset=utf-8";

                // Restore cached headers
                foreach (var header in cached.ResponseHeaders)
                {
                    context.Response.Headers.Append(header.Key, header.Value);
                }

                await context.Response.WriteAsync(cached.Body);
                return;
            }

            // Expired - remove old entry
            _cache.TryRemove(key, out _);
        }

        // Capture the response
        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);
        }
        finally
        {
            // Restore original stream
            context.Response.Body = originalBodyStream;

            // Only cache successful responses (2xx)
            if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
            {
                responseBody.Seek(0, SeekOrigin.Begin);
                var body = await new StreamReader(responseBody).ReadToEndAsync();

                var cachedResponse = new CachedResponse
                {
                    StatusCode = context.Response.StatusCode,
                    Body = body,
                    CreatedAt = DateTime.UtcNow,
                    ResponseHeaders = context.Response.Headers
                        .Where(h => !string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(h => h.Key, h => h.Value.ToString())
                };

                _cache.TryAdd(key, cachedResponse);
                _logger.LogDebug("Cached idempotent response for key: {Key}", key);

                // Write response to original stream
                await context.Response.WriteAsync(body);
            }
            else
            {
                // For non-success responses, just pass through
                responseBody.Seek(0, SeekOrigin.Begin);
                await responseBody.CopyToAsync(originalBodyStream);
            }
        }
    }

    private class CachedResponse
    {
        public int StatusCode { get; init; }
        public string Body { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public Dictionary<string, string> ResponseHeaders { get; init; } = new();
    }
}
public static class IdempotencyMiddlewareExtensions
{
    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<IdempotencyMiddleware>();
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using EBI.ALAS.Api.Common.Models;
using Microsoft.Extensions.Caching.Memory;

namespace EBI.ALAS.Api.Common.Middleware;

// Idempotency middleware backed by IMemoryCache.
//
// Why IMemoryCache (and not IDistributedCache / Redis):
//   IMemoryCache is per-process. Trade-off accepted at design time:
//     * Single-pod deployment only. A multi-replica deployment
//       would need a cross-process cache so a retried request
//       landing on a different pod gets the original's response.
//     * State is lost on restart. A retry that arrives after a
//       process recycle could execute the side effect a second
//       time. The 90s replay window (see TTL below) bounds this.
//   Both are documented in the README §"Cache topology & limits".
//
// Why an in-process cache (and not a process-local ConcurrentDictionary):
//   Same answer as before: we still need TTL eviction so a long-running
//   process doesn't accumulate millions of idempotency entries. IMemoryCache
//   gives us absolute expiration + Size-based LRU out of the box.
//
// Storage envelope:
//   * Status code (int) — replayed as-is.
//   * Body (byte[]) — replayed verbatim. Storing bytes (not string)
//     avoids a UTF-8 round-trip and saves a 2× memory copy.
//   * Headers (small dict) — REPLAYED only after stripping the
//     session-binding headers below. Anything else survives.
//
// Header stripping policy:
//   * `Set-Cookie` — auth endpoints set refreshToken HttpOnly +
//     XSRF-TOKEN cookies here. Replaying those cookies on a replay
//     would re-establish a session for a client that may already be
//     on a different session — high blast-radius.
//   * `Authorization` — same reasoning; carrying a different token
//     on replay would be confusing at best.
//   * `X-XSRF-TOKEN` — anti-forgery header; replaying would couple
//     the replay to the original CSRF challenge state.
//   * Any `X-*` — defence in depth: app-specific headers are
//     dropped by default. Whitelist explicit headers below if
//     a feature genuinely needs cross-request replay.
//
//   Content-Type is set explicitly on replay (always
//   `application/json; charset=utf-8`) so it doesn't need to be
//   carried in the envelope.
//
// TTL:
//   90 seconds (middle of the 60–120s window). The original bug had
//   10 minutes — long enough that a retry that arrives 9 minutes
//   late still triggers the duplicate-side-effect path. 90s matches
//   realistic client retry budgets without that risk.
//
// Cap:
//   Bounded by IMemoryCache.SizeLimit (set to 10_000 in
//   ServiceCollectionExtensions). Each idempotency entry declares
//   its size so the LRU policy can evict under pressure. At 90s TTL
//   and typical request volumes, the working set stays well below
//   this.
public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    private const string IdempotencyKeyHeader = "Idempotency-Key";

    // Replay window. Clients that retry after this TTL are treated
    // as a brand-new request — that's correct, because at that
    // point the original is hours old and any side-effect retry
    // would be semantically different anyway.
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(90);

    // In-process cache key prefix. Kept separate from the JTI
    // blacklist (`revoked:`) so we can enumerate one without the
    // other during diagnostics.
    private const string CacheKeyPrefix = "idem:";

    // Cap for body size we'll cache. 1 MiB is well above any
    // realistic JSON response in this API (loan lists top out
    // around 200 KB raw, 30 KB after brotli). Anything bigger is
    // almost certainly a bug or an attack; refusing to cache it
    // avoids memory pressure and lets the original response
    // proceed without an idempotency replay contract.
    private const int MaxCacheableBodyBytes = 1 * 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Headers we always strip on cache write. Listed by exact
    // name (case-insensitive) — kept as a HashSet for O(1) lookups
    // in the hot path.
    private static readonly HashSet<string> AlwaysStrippedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Set-Cookie",
        "Authorization",
        "X-XSRF-TOKEN"
    };

    // Header prefix we always strip on cache write. Matches the
    // X-* convention; defence in depth so any future app header
    // doesn't accidentally get replayed.
    private const string StrippedHeaderPrefix = "X-";

    public IdempotencyMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only apply to mutating methods. GET / HEAD / OPTIONS are
        // already idempotent at the HTTP level.
        if (!HttpMethods.IsPost(context.Request.Method) &&
            !HttpMethods.IsPut(context.Request.Method) &&
            !HttpMethods.IsPatch(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Check for idempotency key. Missing → proceed without
        // idempotency contract (callers that want the contract
        // must send the header).
        if (!context.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var idempotencyKey) ||
            string.IsNullOrWhiteSpace(idempotencyKey))
        {
            await _next(context);
            return;
        }

        var key = idempotencyKey.ToString();

        // Validate key format (reasonable length). The 8–128 range
        // matches the original middleware so existing clients keep
        // working; UUIDs and ULIDs both fit comfortably.
        if (key.Length < 8 || key.Length > 128)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                ApiResponse.ErrorResponse("Idempotency-Key must be between 8 and 128 characters"));
            return;
        }

        var cacheKey = CacheKeyPrefix + key;

        // Check in-process cache for a prior response.
        if (_cache.TryGetValue(cacheKey, out CachedIdempotentResponse? cached) && cached is not null)
        {
            _logger.LogInformation("Idempotency hit for key: {Key}", key);
            await WriteReplayAsync(context, cached);
            return;
        }

        // Capture the response.
        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);
        }
        finally
        {
            // Restore original stream.
            context.Response.Body = originalBodyStream;

            // Only cache successful responses (2xx). 4xx / 5xx
            // are typically transient (validation error, DB
            // conflict) — caching them would amplify the error
            // across every retry.
            if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
            {
                // Cap body size. Larger responses (e.g. file
                // downloads, if anyone ever wires that up) skip
                // the cache and stream straight through. The
                // cap protects the in-process cache from a 100 MB
                // payload accidentally triggering a 100 MB
                // allocation.
                if (responseBody.Length > MaxCacheableBodyBytes)
                {
                    _logger.LogWarning(
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

                    _cache.Set(
                        cacheKey,
                        entry,
                        new MemoryCacheEntryOptions
                        {
                            // Absolute expiration — sliding expiration
                            // would let a hot key live forever in
                            // the cache even though we want it gone
                            // after the 90s replay window.
                            AbsoluteExpirationRelativeToNow = CacheDuration,

                            // Size accounting for the IMemoryCache LRU.
                            // Rough heuristic: body length in KB,
                            // floored at 1 so even empty-body
                            // responses occupy a slot. The 1 MiB
                            // cap on body size above keeps the
                            // worst-case contribution to 1024
                            // size-units per entry.
                            Size = Math.Max(1, bodyBytes.Length / 1024)
                        });

                    _logger.LogDebug("Cached idempotent response for key: {Key} ({Bytes} bytes)", key, bodyBytes.Length);
                }
            }

            // Write the original response to the real stream —
            // both on cache-hit (we restored the stream first)
            // and on cache-miss (we just finished capturing).
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
            // Join multi-valued headers (rare on responses, but
            // possible — e.g. Vary). StringValues is value-type
            // but ToString() can be null on empty entries — guard.
            var joined = header.Value.ToString();
            if (!string.IsNullOrEmpty(joined))
                result[header.Key] = joined;
        }
        return result;
    }

    private static async Task WriteReplayAsync(HttpContext context, CachedIdempotentResponse cached)
    {
        context.Response.StatusCode = cached.StatusCode;
        // Always emit a deterministic content-type on replay —
        // the original response's content-type is already
        // application/json; charset=utf-8 for our API, and
        // replaying it doesn't change anything, but hard-coding
        // it removes a small attack surface (e.g. someone
        // setting content-type: text/html on a cached entry).
        context.Response.ContentType = "application/json; charset=utf-8";

        foreach (var header in cached.Headers)
        {
            // Defensive: even though we stripped these on cache
            // write, a stale entry from an older version of this
            // middleware might still carry them. Strip on read
            // too.
            if (AlwaysStrippedHeaders.Contains(header.Key)) continue;
            if (header.Key.StartsWith(StrippedHeaderPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            context.Response.Headers.Append(header.Key, header.Value);
        }

        await context.Response.Body.WriteAsync(cached.Body);
    }

    // Wire envelope. Kept private and immutable so the JSON
    // contract is owned entirely by this file. Renaming a
    // property is a breaking change to the cache payload
    // format — old entries would be deserialized with the
    // default value and treated as a cache miss, which is
    // safe-but-noisy. If you ever need to migrate, bump the
    // type name (e.g. `CachedIdempotentResponseV2`) and add a
    // try/oldtype fallthrough in the deserializer.
    private sealed class CachedIdempotentResponse
    {
        public int StatusCode { get; init; }
        public byte[] Body { get; init; } = Array.Empty<byte>();
        public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public static class IdempotencyMiddlewareExtensions
{
    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<IdempotencyMiddleware>();
    }
}

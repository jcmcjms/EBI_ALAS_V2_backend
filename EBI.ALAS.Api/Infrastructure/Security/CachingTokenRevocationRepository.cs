using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Auth;
using Microsoft.Extensions.Caching.Distributed;

namespace EBI.ALAS.Api.Infrastructure.Security;

// Cross-pod caching decorator for ITokenRevocationRepository.
//
// Why IDistributedCache (Redis) and not IMemoryCache:
//   IMemoryCache lives per-pod. After a logout on pod A, only pod A
//   learns that the JTI is revoked — pod B will happily accept the
//   same revoked token until it expires (15 min). On a multi-replica
//   deployment that's a real auth bypass, not just a perf issue.
//
//   IDistributedCache (Redis) gives every pod the same view of the
//   blacklist. A revoke on any pod is visible to every other pod
//   within milliseconds.
//
// What this decorator does:
//   * `RevokeTokenAsync` — durable DB write first (never lose a
//     revocation), then prime the distributed cache with the
//     token's remaining lifetime so subsequent IsTokenRevokedAsync
//     hits on any pod skip the DB.
//   * `IsTokenRevokedAsync` — read-through cache with a sensible
//     fallback TTL when the original expiresAt isn't known.
//
// Why a 1-byte payload:
//   The stored value is just a flag (true = revoked, false = not on
//   the blacklist at lookup time, but we don't store false). Redis
//   SET/GET of 1 byte is the cheapest possible op.
//
// Why two TTLs:
//   * FallbackTtl: the default we use on cache miss, because we
//     don't always have the original expiresAt. Matches the JWT
//     access-token window (15 min).
//   * MaxTtl: hard ceiling so a pathological expiresAt can't pin
//     a JTI in Redis forever.
public sealed class CachingTokenRevocationRepository : ITokenRevocationRepository
{
    // Distributed-cache key prefix for the JTI blacklist. Kept
    // separate from other IDistributedCache users (e.g. the
    // idempotency middleware uses `idem:`) so we can apply a
    // targeted maxmemory / TTL policy per use-case if we ever need
    // to via Redis key-space notifications.
    private const string CacheKeyPrefix = "revoked:";

    // 1-byte payload. 0x01 == revoked. We never cache "not revoked"
    // because that would pin a non-event in Redis for 15 min and
    // shadow real revocations.
    private static readonly byte[] RevokedTruePayload = [0x01];

    private readonly ITokenRevocationRepository _inner;
    private readonly IDistributedCache _cache;
    private readonly ITimeProvider _timeProvider;
    private readonly ILogger<CachingTokenRevocationRepository> _logger;

    // Default TTL used when a token's remaining lifetime cannot be
    // determined (e.g., during housekeeping checks). Matches the
    // JWT access-token window.
    private static readonly TimeSpan FallbackTtl = TimeSpan.FromMinutes(15);

    // Hard ceiling for any cache entry — protects against pathological
    // expiresAt values.
    private static readonly TimeSpan MaxTtl = TimeSpan.FromDays(1);

    public CachingTokenRevocationRepository(
        ITokenRevocationRepository inner,
        IDistributedCache cache,
        ITimeProvider timeProvider,
        ILogger<CachingTokenRevocationRepository> logger)
    {
        _inner = inner;
        _cache = cache;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<bool> IsTokenRevokedAsync(string tokenId)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);

        var cacheKey = CacheKeyPrefix + tokenId;

        // Read-through. We only cache positive revocations, so a hit
        // here is always "revoked = true". A miss falls through to
        // the durable store.
        var cached = await _cache.GetAsync(cacheKey);
        if (cached is { Length: 1 } && cached[0] == 0x01)
        {
            _logger.LogDebug("JTI cache hit for {TokenIdPrefix} (revoked=true)",
                Truncate(tokenId));
            return true;
        }

        var fromStore = await _inner.IsTokenRevokedAsync(tokenId);

        // Prime cache with a sensible default TTL. We don't know the
        // original expiresAt here (only the JTI), so we use the
        // fallback window which matches the access-token expiry. This
        // is the common case for every authenticated request once a
        // token has been validated.
        //
        // Only positive revocations are cached. Caching "not revoked"
        // would pin a non-event in Redis and risk shadowing a later
        // revocation if the DB write lands before our cache write.
        if (fromStore)
        {
            await _cache.SetAsync(
                cacheKey,
                RevokedTruePayload,
                BuildEntryOptions(FallbackTtl));
        }

        _logger.LogDebug("JTI cache miss for {TokenIdPrefix} (revoked={Revoked})",
            Truncate(tokenId), fromStore);

        return fromStore;
    }

    public async Task RevokeTokenAsync(string tokenId, int userId, DateTime expiresAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);

        // Durable write first — never lose a revocation. If the DB
        // write throws we MUST NOT poison the cache with a revocation
        // that was never persisted.
        await _inner.RevokeTokenAsync(tokenId, userId, expiresAt);

        var cacheKey = CacheKeyPrefix + tokenId;
        var ttl = expiresAt - _timeProvider.UtcNow;

        // Only cache if the token has a positive remaining lifetime.
        if (ttl > TimeSpan.Zero)
        {
            var boundedTtl = ttl > MaxTtl ? MaxTtl : ttl;
            await _cache.SetAsync(
                cacheKey,
                RevokedTruePayload,
                BuildEntryOptions(boundedTtl));
        }

        _logger.LogInformation(
            "Token {TokenIdPrefix} revoked for user {UserId} (cache TTL {TtlSeconds}s)",
            Truncate(tokenId), userId,
            (int)(ttl > TimeSpan.Zero ? Math.Min(ttl.TotalSeconds, MaxTtl.TotalSeconds) : 0));
    }

    public Task<int> CleanupExpiredTokensAsync() => _inner.CleanupExpiredTokensAsync();

    private static DistributedCacheEntryOptions BuildEntryOptions(TimeSpan ttl) =>
        new()
        {
            // Absolute expiration so the entry expires at a fixed
            // wall-clock point regardless of access patterns. Sliding
            // expiration would let a frequently-validated revoked
            // JTI live forever in Redis.
            AbsoluteExpirationRelativeToNow = ttl
        };

    private static string Truncate(string value) =>
        value.Length <= 8 ? value : value[..8] + "…";
}

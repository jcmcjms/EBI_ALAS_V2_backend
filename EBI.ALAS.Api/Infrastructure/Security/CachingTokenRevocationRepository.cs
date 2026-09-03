using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Auth;
using Microsoft.Extensions.Caching.Memory;

namespace EBI.ALAS.Api.Infrastructure.Security;
public sealed class CachingTokenRevocationRepository : ITokenRevocationRepository
{
    private const string CacheKeyPrefix = "revoked:";

    private readonly ITokenRevocationRepository _inner;
    private readonly IMemoryCache _cache;
    private readonly ITimeProvider _timeProvider;
    private readonly ILogger<CachingTokenRevocationRepository> _logger;

    // Default TTL used when a token's remaining lifetime cannot be determined
    // (e.g., during housekeeping checks). Matches the JWT access-token window.
    private static readonly TimeSpan FallbackTtl = TimeSpan.FromMinutes(15);

    // Hard ceiling for any cache entry — protects against pathological expiresAt values.
    private static readonly TimeSpan MaxTtl = TimeSpan.FromDays(1);
    public CachingTokenRevocationRepository(
        ITokenRevocationRepository inner,
        IMemoryCache cache,
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

        if (_cache.TryGetValue(cacheKey, out bool cachedResult))
        {
            _logger.LogDebug("JTI cache hit for {TokenIdPrefix} (revoked={Revoked})",
                Truncate(tokenId), cachedResult);
            return cachedResult;
        }

        var fromStore = await _inner.IsTokenRevokedAsync(tokenId);

        // Prime cache with a sensible default TTL. We don't know the original
        // expiresAt here (only the JTI), so we use the fallback window which
        // matches the access-token expiry. This is the common case for
        // every authenticated request once a token has been validated.
        _cache.Set(cacheKey, fromStore, BuildEntryOptions(FallbackTtl));

        _logger.LogDebug("JTI cache miss for {TokenIdPrefix} (revoked={Revoked})",
            Truncate(tokenId), fromStore);

        return fromStore;
    }
    public async Task RevokeTokenAsync(string tokenId, int userId, DateTime expiresAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);

        // Durable write first — never lose a revocation.
        await _inner.RevokeTokenAsync(tokenId, userId, expiresAt);

        var cacheKey = CacheKeyPrefix + tokenId;
        var ttl = expiresAt - _timeProvider.UtcNow;

        // Only cache if the token has a positive remaining lifetime.
        if (ttl > TimeSpan.Zero)
        {
            var boundedTtl = ttl > MaxTtl ? MaxTtl : ttl;
            _cache.Set(cacheKey, true, BuildEntryOptions(boundedTtl));
        }

        _logger.LogInformation(
            "Token {TokenIdPrefix} revoked for user {UserId} (cache TTL {TtlSeconds}s)",
            Truncate(tokenId), userId, (int)(ttl > TimeSpan.Zero ? Math.Min(ttl.TotalSeconds, MaxTtl.TotalSeconds) : 0));
    }
    public Task CleanupExpiredTokensAsync() => _inner.CleanupExpiredTokensAsync();

    private static MemoryCacheEntryOptions BuildEntryOptions(TimeSpan ttl) =>
        new()
        {
            AbsoluteExpirationRelativeToNow = ttl,
            // Size-based eviction is enabled on the cache (see
            // ServiceCollectionExtensions.AddMemoryCache — SizeLimit = 10k).
            // Every entry MUST declare a Size or
            // IMemoryCache.Set throws InvalidOperationException at runtime.
            // A JTI revocation lookup is a single bool, so 1 unit is fine.
            Size = 1,
            Priority = CacheItemPriority.Normal
        };

    private static string Truncate(string value) =>
        value.Length <= 8 ? value : value[..8] + "…";
}
using System.Text.Json;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Auth;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace EBI.ALAS.Api.Infrastructure.Security;

/// <summary>
/// Two-tier caching decorator for ITokenRevocationRepository.
///
/// Now backed by both IMemoryCache (L1, per-process) and
/// IDistributedCache (L2, Redis when configured, in-memory fallback).
/// This enables multi-pod deployment — a token revoked on one pod
/// is visible to all pods via the shared L2 cache.
///
/// Architecture:
///   L1 (IMemoryCache): per-process, fastest, lost on restart
///   L2 (IDistributedCache): shared across pods (Redis), survives restarts
///   DB (inner repo): durable source of truth
///
/// Flow:
///   RevokeTokenAsync: DB write → L2 write → L1 write
///   IsTokenRevokedAsync: L1 hit → L2 hit → DB fallback → prime L2+L1
/// </summary>
public sealed class CachingTokenRevocationRepository : ITokenRevocationRepository
{
    private const string CacheKeyPrefix = "revoked:";
    private const string DistributedCachePrefix = "ALAS_revoked:";

    private static readonly byte[] RevokedTruePayload = [0x01];
    private const int EntrySize = 1;

    private readonly ITokenRevocationRepository _inner;
    private readonly IMemoryCache _cache;
    private readonly IDistributedCache _distributedCache;
    private readonly ITimeProvider _timeProvider;
    private readonly ILogger<CachingTokenRevocationRepository> _logger;

    private static readonly TimeSpan FallbackTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaxTtl = TimeSpan.FromDays(1);

    public CachingTokenRevocationRepository(
        ITokenRevocationRepository inner,
        IMemoryCache cache,
        IDistributedCache distributedCache,
        ITimeProvider timeProvider,
        ILogger<CachingTokenRevocationRepository> logger)
    {
        _inner = inner;
        _cache = cache;
        _distributedCache = distributedCache;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<bool> IsTokenRevokedAsync(string tokenId)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);

        var cacheKey = CacheKeyPrefix + tokenId;
        var distributedKey = DistributedCachePrefix + tokenId;

        // L1: Per-process memory cache (fastest)
        if (_cache.TryGetValue(cacheKey, out byte[]? cached) &&
            cached is { Length: 1 } &&
            cached[0] == 0x01)
        {
            _logger.LogDebug("JTI L1 cache hit for {TokenIdPrefix} (revoked=true)",
                Truncate(tokenId));
            return true;
        }

        // L2: Distributed cache (Redis, shared across pods)
        try
        {
            var distributedValue = await _distributedCache.GetAsync(distributedKey);
            if (distributedValue is { Length: 1 } && distributedValue[0] == 0x01)
            {
                _logger.LogDebug("JTI L2 cache hit for {TokenIdPrefix} (revoked=true)",
                    Truncate(tokenId));
                // Prime L1 for subsequent requests on this pod
                _cache.Set(cacheKey, RevokedTruePayload, BuildEntryOptions(FallbackTtl));
                return true;
            }
        }
        catch (Exception ex)
        {
            // L2 failure is non-fatal — fall through to DB
            _logger.LogWarning(ex, "JTI L2 cache read failed for {TokenIdPrefix}, falling back to DB",
                Truncate(tokenId));
        }

        // DB: Durable source of truth
        var fromStore = await _inner.IsTokenRevokedAsync(tokenId);

        if (fromStore)
        {
            // Prime both L2 and L1
            var entryOptions = BuildEntryOptions(FallbackTtl);
            _cache.Set(cacheKey, RevokedTruePayload, entryOptions);

            try
            {
                var distributedOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = FallbackTtl
                };
                await _distributedCache.SetAsync(distributedKey, RevokedTruePayload, distributedOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "JTI L2 cache write failed for {TokenIdPrefix}",
                    Truncate(tokenId));
            }
        }

        _logger.LogDebug("JTI cache miss for {TokenIdPrefix} (revoked={Revoked})",
            Truncate(tokenId), fromStore);

        return fromStore;
    }

    public async Task RevokeTokenAsync(string tokenId, int userId, DateTime expiresAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);

        // Durable write first — never lose a revocation
        await _inner.RevokeTokenAsync(tokenId, userId, expiresAt);

        var cacheKey = CacheKeyPrefix + tokenId;
        var distributedKey = DistributedCachePrefix + tokenId;
        var ttl = expiresAt - _timeProvider.UtcNow;
        var boundedTtl = ttl > TimeSpan.Zero ? (ttl > MaxTtl ? MaxTtl : ttl) : FallbackTtl;

        // Prime L1
        _cache.Set(cacheKey, RevokedTruePayload, BuildEntryOptions(boundedTtl));

        // Prime L2 (Redis)
        try
        {
            var distributedOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = boundedTtl
            };
            await _distributedCache.SetAsync(distributedKey, RevokedTruePayload, distributedOptions);
        }
        catch (Exception ex)
        {
            // L2 failure is non-fatal — the DB write and L1 are already done
            _logger.LogWarning(ex, "JTI L2 cache write failed for {TokenIdPrefix}",
                Truncate(tokenId));
        }

        _logger.LogInformation(
            "Token {TokenIdPrefix} revoked for user {UserId} (L1+L2+DB, TTL {TtlSeconds}s)",
            Truncate(tokenId), userId,
            (int)boundedTtl.TotalSeconds);
    }

    public Task<int> CleanupExpiredTokensAsync() => _inner.CleanupExpiredTokensAsync();

    private static MemoryCacheEntryOptions BuildEntryOptions(TimeSpan ttl) =>
        new()
        {
            AbsoluteExpirationRelativeToNow = ttl,
            Size = EntrySize
        };

    private static string Truncate(string value) =>
        value.Length <= 8 ? value : value[..8] + "…";
}

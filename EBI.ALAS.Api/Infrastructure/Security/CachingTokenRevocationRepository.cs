using System.Diagnostics;
using System.Diagnostics.Metrics;
using EBI.ALAS.Api.Shared.Time;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;
namespace EBI.ALAS.Api.Infrastructure.Security;
public sealed class CachingTokenRevocationRepository : ITokenRevocationRepository
{
    private const string CacheKeyPrefix = "revoked:";
    private const string DistributedCachePrefix = "ALAS_revoked:";
    private static readonly byte[] RevokedTruePayload = [0x01];
    private const int EntrySize = 1;
    private readonly ITokenRevocationRepository _inner;
    private readonly IMemoryCache _cache;
    private readonly GarnetConnectionMultiplexer? _multiplexerFactory;
    private readonly IDistributedCache? _distributedCache;
    private readonly ITimeProvider _timeProvider;
    private readonly ILogger<CachingTokenRevocationRepository> _logger;
    private static readonly TimeSpan FallbackTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaxTtl = TimeSpan.FromDays(1);
    private static readonly Meter Meter = new("EBI.ALAS.TokenCache", "1.0.0");
    private static readonly Counter<long> L2Hits = Meter.CreateCounter<long>(
        "token_cache.l2.hits", description: "L2 Garnet cache hits for JTI revocation");
    private static readonly Counter<long> L2Misses = Meter.CreateCounter<long>(
        "token_cache.l2.misses", description: "L2 Garnet cache misses for JTI revocation");
    private static readonly Counter<long> L2Writes = Meter.CreateCounter<long>(
        "token_cache.l2.writes", description: "L2 Garnet cache writes for JTI revocation");
    private static readonly Histogram<double> L2Latency = Meter.CreateHistogram<double>(
        "token_cache.l2.latency_ms", unit: "ms", description: "L2 Garnet operation latency");
    public CachingTokenRevocationRepository(
        ITokenRevocationRepository inner,
        IMemoryCache cache,
        GarnetConnectionMultiplexer multiplexerFactory,
        ITimeProvider timeProvider,
        ILogger<CachingTokenRevocationRepository> logger)
    {
        _inner = inner;
        _cache = cache;
        _multiplexerFactory = multiplexerFactory;
        _distributedCache = null;
        _timeProvider = timeProvider;
        _logger = logger;
    }
    public CachingTokenRevocationRepository(
        ITokenRevocationRepository inner,
        IMemoryCache cache,
        IDistributedCache distributedCache,
        ITimeProvider timeProvider,
        ILogger<CachingTokenRevocationRepository> logger)
    {
        _inner = inner;
        _cache = cache;
        _multiplexerFactory = null;
        _distributedCache = distributedCache;
        _timeProvider = timeProvider;
        _logger = logger;
    }
    public async Task<bool> IsTokenRevokedAsync(string tokenId)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);
        var cacheKey = CacheKeyPrefix + tokenId;
        var distributedKey = DistributedCachePrefix + tokenId;
        if (_cache.TryGetValue(cacheKey, out byte[]? cached) &&
            cached is { Length: 1 } &&
            cached[0] == 0x01)
        {
            _logger.LogDebug("JTI L1 cache hit for {TokenIdPrefix} (revoked=true)",
                Truncate(tokenId));
            return true;
        }
        try
        {
            byte[]? distributedValue = null;
            if (_multiplexerFactory is not null)
            {
                var db = await GetDatabaseAsync();
                var sw = Stopwatch.StartNew();
                var redisValue = await db.StringGetAsync(distributedKey);
                distributedValue = redisValue.HasValue ? (byte[])redisValue! : null;
                sw.Stop();
                L2Latency.Record(sw.ElapsedMilliseconds,
                    new KeyValuePair<string, object?>("operation", "GET"));
            }
            else if (_distributedCache is not null)
            {
                distributedValue = await _distributedCache.GetAsync(distributedKey);
            }
            if (distributedValue is { Length: 1 } && distributedValue[0] == 0x01)
            {
                L2Hits.Add(1);
                _logger.LogDebug("JTI L2 cache hit for {TokenIdPrefix} (revoked=true)",
                    Truncate(tokenId));
                _cache.Set(cacheKey, RevokedTruePayload, BuildEntryOptions(FallbackTtl));
                return true;
            }
            L2Misses.Add(1);
        }
        catch (Exception ex)
        {
            L2Misses.Add(1);
            _logger.LogWarning(ex, "JTI L2 cache read failed for {TokenIdPrefix}, falling back to DB",
                Truncate(tokenId));
        }
        var fromStore = await _inner.IsTokenRevokedAsync(tokenId);
        if (fromStore)
        {
            var entryOptions = BuildEntryOptions(FallbackTtl);
            _cache.Set(cacheKey, RevokedTruePayload, entryOptions);
            await PrimeL2CacheAsync(distributedKey, FallbackTtl, tokenId);
        }
        _logger.LogDebug("JTI cache miss for {TokenIdPrefix} (revoked={Revoked})",
            Truncate(tokenId), fromStore);
        return fromStore;
    }
    public async Task RevokeTokenAsync(string tokenId, int userId, DateTime expiresAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);
        await _inner.RevokeTokenAsync(tokenId, userId, expiresAt);
        var cacheKey = CacheKeyPrefix + tokenId;
        var distributedKey = DistributedCachePrefix + tokenId;
        var ttl = expiresAt - _timeProvider.UtcNow;
        var boundedTtl = ttl > TimeSpan.Zero ? (ttl > MaxTtl ? MaxTtl : ttl) : FallbackTtl;
        _cache.Set(cacheKey, RevokedTruePayload, BuildEntryOptions(boundedTtl));
        await PrimeL2CacheAsync(distributedKey, boundedTtl, tokenId);
        _logger.LogInformation(
            "Token {TokenIdPrefix} revoked for user {UserId} (L1+L2+DB, TTL {TtlSeconds}s)",
            Truncate(tokenId), userId,
            (int)boundedTtl.TotalSeconds);
    }
    public Task<int> CleanupExpiredTokensAsync() => _inner.CleanupExpiredTokensAsync();
    private async Task PrimeL2CacheAsync(string distributedKey, TimeSpan ttl, string tokenId)
    {
        try
        {
            if (_multiplexerFactory is not null)
            {
                var db = await GetDatabaseAsync();
                var sw = Stopwatch.StartNew();
                await db.StringSetAsync(
                    distributedKey,
                    RevokedTruePayload,
                    ttl,
                    flags: CommandFlags.FireAndForget);
                sw.Stop();
                L2Writes.Add(1);
                L2Latency.Record(sw.ElapsedMilliseconds,
                    new KeyValuePair<string, object?>("operation", "SET"));
            }
            else if (_distributedCache is not null)
            {
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl
                };
                await _distributedCache.SetAsync(distributedKey, RevokedTruePayload, options);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JTI L2 cache write failed for {TokenIdPrefix}",
                Truncate(tokenId));
        }
    }
    private async Task<IDatabase> GetDatabaseAsync()
    {
        var connection = await _multiplexerFactory!.GetConnectionAsync();
        return connection.GetDatabase();
    }
    private static MemoryCacheEntryOptions BuildEntryOptions(TimeSpan ttl) =>
        new()
        {
            AbsoluteExpirationRelativeToNow = ttl,
            Size = EntrySize
        };
    private static string Truncate(string value) =>
        value.Length <= 8 ? value : value[..8] + "\u2026";
}

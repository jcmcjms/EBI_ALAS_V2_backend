using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EBI.ALAS.Api.Infrastructure.Caching;

/// <summary>
/// High-performance <see cref="IDistributedCache"/> implementation backed by
/// a shared <see cref="IConnectionMultiplexer"/> to Garnet.
///
/// Key optimizations over the default <c>Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache</c>:
/// 1. Shared multiplexer — no per-instance connection overhead.
/// 2. Direct <see cref="IDatabase"/> calls — bypasses the intermediate abstraction.
/// 3. Cache hit/miss metrics via OpenTelemetry <see cref="Meter"/>.
/// </summary>
public sealed class GarnetDistributedCache : IDistributedCache, IDisposable
{
    private readonly GarnetConnectionMultiplexer _multiplexerFactory;
    private readonly GarnetOptions _options;
    private readonly ILogger<GarnetDistributedCache> _logger;

    // OpenTelemetry metrics
    private static readonly Meter Meter = new("EBI.ALAS.Caching", "1.0.0");
    private static readonly Counter<long> CacheHits = Meter.CreateCounter<long>(
        "cache.garnet.hits", description: "Number of Garnet cache hits");
    private static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>(
        "cache.garnet.misses", description: "Number of Garnet cache misses");
    private static readonly Histogram<double> CacheLatency = Meter.CreateHistogram<double>(
        "cache.garnet.latency_ms", unit: "ms", description: "Garnet cache operation latency");

    public GarnetDistributedCache(
        GarnetConnectionMultiplexer multiplexerFactory,
        IOptions<GarnetOptions> options,
        ILogger<GarnetDistributedCache> logger)
    {
        _multiplexerFactory = multiplexerFactory;
        _options = options.Value;
        _logger = logger;
    }

    public byte[]? Get(string key) =>
        GetAsync(key, CancellationToken.None).GetAwaiter().GetResult();

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var prefixedKey = _options.InstanceName + key;
        var sw = Stopwatch.StartNew();

        try
        {
            var db = await GetDatabaseAsync();
            var value = await db.StringGetAsync(prefixedKey);

            sw.Stop();
            RecordLatency("GET", sw.ElapsedMilliseconds);

            if (value.HasValue)
            {
                CacheHits.Add(1);
                return (byte[])value!;
            }

            CacheMisses.Add(1);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Garnet GET failed for key {Key}", TruncateKey(key));
            CacheMisses.Add(1);
            return null;
        }
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
        SetAsync(key, value, options, CancellationToken.None).GetAwaiter().GetResult();

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        var prefixedKey = _options.InstanceName + key;
        var expiry = ResolveExpiry(options);
        var sw = Stopwatch.StartNew();

        try
        {
            var db = await GetDatabaseAsync();
            await db.StringSetAsync(prefixedKey, value, expiry);

            sw.Stop();
            RecordLatency("SET", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // Non-fatal — cache writes are best-effort for acceleration.
            // The DB is the source of truth for all state.
            _logger.LogWarning(ex, "Garnet SET failed for key {Key}", TruncateKey(key));
        }
    }

    public void Refresh(string key) =>
        RefreshAsync(key, CancellationToken.None).GetAwaiter().GetResult();

    public Task RefreshAsync(string key, CancellationToken token = default)
    {
        // TTL refresh is a no-op — entries use absolute expiry only.
        // Redis/Garnet doesn't natively support sliding expiration.
        return Task.CompletedTask;
    }

    public void Remove(string key) =>
        RemoveAsync(key, CancellationToken.None).GetAwaiter().GetResult();

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var prefixedKey = _options.InstanceName + key;

        try
        {
            var db = await GetDatabaseAsync();
            await db.KeyDeleteAsync(prefixedKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Garnet DEL failed for key {Key}", TruncateKey(key));
        }
    }

    private async Task<IDatabase> GetDatabaseAsync()
    {
        var connection = await _multiplexerFactory.GetConnectionAsync();
        return connection.GetDatabase(_options.Database);
    }

    private static TimeSpan? ResolveExpiry(DistributedCacheEntryOptions options)
    {
        if (options.AbsoluteExpirationRelativeToNow.HasValue)
            return options.AbsoluteExpirationRelativeToNow;

        if (options.AbsoluteExpiration.HasValue)
            return options.AbsoluteExpiration.Value - DateTimeOffset.UtcNow;

        if (options.SlidingExpiration.HasValue)
            return options.SlidingExpiration;

        return null;
    }

    private static void RecordLatency(string operation, double milliseconds)
    {
        CacheLatency.Record(milliseconds,
            new KeyValuePair<string, object?>("operation", operation));
    }

    private static string TruncateKey(string key) =>
        key.Length <= 16 ? key : key[..16] + "...";

    public void Dispose()
    {
        // Meter is static — don't dispose per-instance.
        // The multiplexer is singleton and disposed by the host.
    }
}

namespace EBI.ALAS.Api.Infrastructure.Caching;

/// <summary>
/// Strongly-typed configuration for Garnet cache client tuning.
/// Bound from the "Garnet" section in appsettings.json.
/// </summary>
public sealed class GarnetOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Garnet";

    /// <summary>Redis-compatible connection string for Garnet.</summary>
    public string ConnectionString { get; set; } = "127.0.0.1:6379";

    /// <summary>Key prefix for all cache entries (default: "ALAS_").</summary>
    public string InstanceName { get; set; } = "ALAS_";

    /// <summary>
    /// Socket send/receive buffer size in bytes. Garnet benefits from
    /// larger buffers for bursty workloads. Default: 64 KB.
    /// </summary>
    public int BufferSize { get; set; } = 65536;

    /// <summary>
    /// Number of interactive operations to queue before forcing a flush.
    /// Higher values improve throughput at the cost of latency. Default: 100.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Connection timeout in milliseconds. Default: 10000.</summary>
    public int ConnectTimeoutMs { get; set; } = 10000;

    /// <summary>Synchronous operation timeout in milliseconds. Default: 5000.</summary>
    public int SyncTimeoutMs { get; set; } = 5000;

    /// <summary>Asynchronous operation timeout in milliseconds. Default: 5000.</summary>
    public int AsyncTimeoutMs { get; set; } = 5000;

    /// <summary>Keep-alive interval in seconds. Default: 30.</summary>
    public int KeepAliveSeconds { get; set; } = 30;

    /// <summary>Maximum number of connection retry attempts. Default: 5.</summary>
    public int ConnectRetry { get; set; } = 5;

    /// <summary>
    /// Whether to abort immediately on initial connection failure.
    /// False is recommended for resilience — the client will retry.
    /// </summary>
    public bool AbortOnConnectFail { get; set; }

    /// <summary>
    /// Redis database index to use. Default: 0.
    /// </summary>
    public int Database { get; set; }

    /// <summary>
    /// Enable pub/sub support for SignalR backplane. Default: true.
    /// </summary>
    public bool EnablePubSub { get; set; } = true;
}

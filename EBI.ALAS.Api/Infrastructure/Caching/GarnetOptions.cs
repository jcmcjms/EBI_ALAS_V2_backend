namespace EBI.ALAS.Api.Infrastructure.Caching;
public sealed class GarnetOptions
{
    public const string SectionName = "Garnet";
    public string ConnectionString { get; set; } = "127.0.0.1:6379";
    public string InstanceName { get; set; } = "ALAS_";
    public int BufferSize { get; set; } = 65536;
    public int BatchSize { get; set; } = 100;
    public int ConnectTimeoutMs { get; set; } = 10000;
    public int SyncTimeoutMs { get; set; } = 5000;
    public int AsyncTimeoutMs { get; set; } = 5000;
    public int KeepAliveSeconds { get; set; } = 30;
    public int ConnectRetry { get; set; } = 5;
    public bool AbortOnConnectFail { get; set; }
    public int Database { get; set; }
    public bool EnablePubSub { get; set; } = true;
}

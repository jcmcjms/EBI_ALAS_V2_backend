using Microsoft.Extensions.Options;
using StackExchange.Redis;
namespace EBI.ALAS.Api.Infrastructure.Caching;
public sealed class GarnetConnectionMultiplexer : IDisposable
{
    private readonly Lazy<Task<IConnectionMultiplexer>> _lazyConnection;
    private readonly GarnetOptions _options;
    private readonly ILogger<GarnetConnectionMultiplexer> _logger;
    private IConnectionMultiplexer? _connection;
    public GarnetConnectionMultiplexer(
        IOptions<GarnetOptions> options,
        ILogger<GarnetConnectionMultiplexer> logger)
    {
        _options = options.Value;
        _logger = logger;
        _lazyConnection = new Lazy<Task<IConnectionMultiplexer>>(
            CreateConnectionAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }
    public async Task<IConnectionMultiplexer> GetConnectionAsync()
    {
        _connection ??= await _lazyConnection.Value;
        return _connection;
    }
    private async Task<IConnectionMultiplexer> CreateConnectionAsync()
    {
        var configOptions = BuildConfigurationOptions();
        _logger.LogInformation(
            "Connecting to Garnet at {ConnectionString} (buffer={BufferSize}, retry={ConnectRetry})",
            _options.ConnectionString, _options.BufferSize, _options.ConnectRetry);
        var multiplexer = await ConnectionMultiplexer.ConnectAsync(configOptions);
        WireConnectionEvents(multiplexer);
        var endpoints = multiplexer.GetEndPoints();
        _logger.LogInformation(
            "Garnet connection established. Endpoints: {Endpoints}, DB: {Database}",
            string.Join(", ", endpoints.Select(e => e.ToString())), _options.Database);
        return multiplexer;
    }
    private ConfigurationOptions BuildConfigurationOptions()
    {
        var configOptions = ConfigurationOptions.Parse(_options.ConnectionString);
        configOptions.AbortOnConnectFail = _options.AbortOnConnectFail;
        configOptions.ConnectTimeout = _options.ConnectTimeoutMs;
        configOptions.SyncTimeout = _options.SyncTimeoutMs;
        configOptions.AsyncTimeout = _options.AsyncTimeoutMs;
        configOptions.KeepAlive = _options.KeepAliveSeconds;
        configOptions.ConnectRetry = _options.ConnectRetry;
        configOptions.DefaultDatabase = _options.Database;
        configOptions.ReconnectRetryPolicy = new ExponentialRetry(5000);
        configOptions.SocketManager = new SocketManager();
        return configOptions;
    }
    private void WireConnectionEvents(IConnectionMultiplexer multiplexer)
    {
        multiplexer.ConnectionFailed += (_, args) =>
        {
            _logger.LogError(args.Exception,
                "Garnet connection failed: {FailureType} on {Endpoint}",
                args.FailureType, args.EndPoint);
        };
        multiplexer.ConnectionRestored += (_, args) =>
        {
            _logger.LogInformation(
                "Garnet connection restored: {ConnectionType} on {Endpoint}",
                args.ConnectionType, args.EndPoint);
        };
        multiplexer.InternalError += (_, args) =>
        {
            _logger.LogError(args.Exception,
                "Garnet internal error: {Origin} on {Endpoint}",
                args.Origin, args.EndPoint);
        };
    }
    public void Dispose()
    {
        if (_connection is { IsConnected: true })
        {
            _connection.Close();
        }
        _connection?.Dispose();
    }
}

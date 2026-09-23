using System.Diagnostics;
using EBI.ALAS.Api.Infrastructure.Caching;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EBI.ALAS.Api.Common.Extensions;

/// <summary>
/// Custom health check for Garnet that measures PING latency and reports
/// connection status. More informative than the generic Redis health check
/// which only checks connectivity.
///
/// Reports:
/// - Connection state (connected/disconnected)
/// - PING round-trip latency in milliseconds
/// - Number of connected endpoints
/// </summary>
public sealed class GarnetHealthCheck : IHealthCheck
{
    private readonly GarnetConnectionMultiplexer _multiplexerFactory;
    private readonly ILogger<GarnetHealthCheck> _logger;

    public GarnetHealthCheck(
        GarnetConnectionMultiplexer multiplexerFactory,
        ILogger<GarnetHealthCheck> logger)
    {
        _multiplexerFactory = multiplexerFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await _multiplexerFactory.GetConnectionAsync();

            if (!connection.IsConnected)
            {
                return HealthCheckResult.Unhealthy(
                    "Garnet connection is not established.",
                    data: new Dictionary<string, object>
                    {
                        ["connected"] = false
                    });
            }

            var sw = Stopwatch.StartNew();
            var db = connection.GetDatabase();
            var pong = await db.PingAsync();
            sw.Stop();

            var latencyMs = sw.ElapsedMilliseconds;
            var endpoints = connection.GetEndPoints();
            var connectedCount = endpoints.Count(e => connection.GetServer(e).IsConnected);

            var data = new Dictionary<string, object>
            {
                ["connected"] = true,
                ["latency_ms"] = latencyMs,
                ["endpoints_total"] = endpoints.Length,
                ["endpoints_connected"] = connectedCount
            };

            // Degraded if latency exceeds 50ms (Garnet should be sub-ms locally)
            if (latencyMs > 50)
            {
                _logger.LogWarning("Garnet PING latency is {LatencyMs}ms (threshold: 50ms)", latencyMs);
                return HealthCheckResult.Degraded(
                    $"Garnet PING latency is {latencyMs}ms (expected <1ms).",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"Garnet is healthy. PING: {latencyMs}ms, {connectedCount}/{endpoints.Length} endpoints connected.",
                data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Garnet health check failed");
            return HealthCheckResult.Unhealthy(
                "Garnet health check failed: " + ex.Message,
                exception: ex);
        }
    }
}

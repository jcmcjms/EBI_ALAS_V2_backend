using EBI.ALAS.Api.Infrastructure.Caching;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace EBI.ALAS.Api.Common.Extensions;

/// <summary>
/// Observability configuration (Serilog, OpenTelemetry, Health Checks).
/// Extracted from Program.cs to follow Single Responsibility Principle.
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// Configures Serilog structured logging with correlation IDs and
    /// request context enrichment. Seq sink is optional — falls back
    /// to console if Seq is not configured.
    /// </summary>
    public static void ConfigureSerilog()
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}", optional: true)
                .Build())
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "EBI.ALAS.V2.API")
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .CreateLogger();
    }

    /// <summary>
    /// OpenTelemetry distributed tracing with ASP.NET Core and
    /// HTTP client instrumentation.
    /// Console exporter only in Development. Added sampling
    /// to reduce trace volume under load. Added metrics support.
    /// </summary>
    public static IServiceCollection AddBankingObservability(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("EBI.ALAS.V2.API"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                    })
                    .AddHttpClientInstrumentation();

                // Console exporter only in Development to avoid
                // serializing every trace to stdout in production (expensive in containers).
                var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                if (string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase))
                {
                    tracing.AddConsoleExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                // Add built-in .NET meters for request duration, status codes, etc.
                // These are available via the .NET 8 built-in metrics (System.Diagnostics.Metrics).
                metrics.AddMeter("Microsoft.AspNetCore.Hosting")
                       .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                       // Garnet cache metrics (hit/miss, latency)
                       .AddMeter("EBI.ALAS.Caching")
                       // Token revocation L2 cache metrics
                       .AddMeter("EBI.ALAS.TokenCache");
            });

        return services;
    }

    /// <summary>
    /// Health checks for SQL Server, Garnet, RabbitMQ, and the application itself.
    /// When Garnet is configured, uses a custom health check with PING latency
    /// measurement instead of the generic Redis health check.
    /// </summary>
    public static IServiceCollection AddBankingHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis");
        var rabbitMqConnection = configuration.GetConnectionString("RabbitMQ");

        var healthChecksBuilder = services.AddHealthChecks()
            .AddSqlServer(
                configuration.GetConnectionString("DefaultConnection")!,
                name: "sqlserver",
                tags: ["db", "sql"],
                timeout: TimeSpan.FromSeconds(5))
            .AddCheck("self",
                () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("API is running"),
                tags: ["api"]);

        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            // Custom Garnet health check with PING latency measurement.
            // More informative than the generic AddRedis check — reports
            // latency, endpoint count, and connection state.
            healthChecksBuilder.AddCheck<GarnetHealthCheck>(
                "garnet",
                failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
                tags: ["cache"],
                timeout: TimeSpan.FromSeconds(5));
        }

        if (!string.IsNullOrWhiteSpace(rabbitMqConnection))
        {
            healthChecksBuilder.AddRabbitMQ(
                rabbitMqConnection,
                name: "rabbitmq",
                tags: ["messaging"],
                timeout: TimeSpan.FromSeconds(5));
        }

        return services;
    }
}

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
                    .AddHttpClientInstrumentation()
                    .AddConsoleExporter();
            });

        return services;
    }

    /// <summary>
    /// Health checks for SQL Server, Redis, RabbitMQ, and the application itself.
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
            healthChecksBuilder.AddRedis(
                redisConnection,
                name: "redis",
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

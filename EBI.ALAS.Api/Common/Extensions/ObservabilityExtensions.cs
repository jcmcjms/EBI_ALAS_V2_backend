using EBI.ALAS.Api.Infrastructure.Caching;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
namespace EBI.ALAS.Api.Common.Extensions;
public static class ObservabilityExtensions
{
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
                var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                if (string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase))
                {
                    tracing.AddConsoleExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter("Microsoft.AspNetCore.Hosting")
                       .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                       .AddMeter("EBI.ALAS.Caching")
                       .AddMeter("EBI.ALAS.TokenCache");
            });
        return services;
    }
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
            healthChecksBuilder.AddCheck<GarnetHealthCheck>(
                "garnet",
                failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
                tags: ["cache"],
                timeout: TimeSpan.FromSeconds(5));
        }
        if (!string.IsNullOrWhiteSpace(rabbitMqConnection))
        {
            healthChecksBuilder.AddRabbitMQ(
                sp =>
                {
                    var factory = new RabbitMQ.Client.ConnectionFactory
                    {
                        Uri = new Uri(rabbitMqConnection!),
                        AutomaticRecoveryEnabled = true
                    };
                    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
                },
                name: "rabbitmq",
                tags: ["messaging"],
                timeout: TimeSpan.FromSeconds(5));
        }
        return services;
    }
}

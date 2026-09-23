using EBI.ALAS.Api.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;

namespace EBI.ALAS.Api.Common.Extensions;

/// <summary>
/// Caching configuration (Garnet distributed cache + output cache).
/// Extracted from Program.cs to follow Single Responsibility Principle.
/// </summary>
public static class CachingExtensions
{
    public static IServiceCollection AddBankingCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDistributedCache(configuration);
        services.AddOutputCaching();
        return services;
    }

    /// <summary>
    /// Configures Garnet-optimized distributed cache for multi-pod deployments.
    /// Falls back to in-memory if Redis/Garnet is not configured (dev/single-pod).
    ///
    /// Key optimizations over default AddStackExchangeRedisCache:
    /// - Shared ConnectionMultiplexer with tuned buffer sizes and reconnect policy
    /// - Custom IDistributedCache with direct IDatabase calls and cache metrics
    /// - Garnet-specific configuration (keepalive, timeouts, socket pool)
    /// </summary>
    private static IServiceCollection AddDistributedCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis");

        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            // Bind Garnet options from configuration section, falling back to
            // the connection string for the endpoint.
            services.Configure<GarnetOptions>(options =>
            {
                configuration.GetSection(GarnetOptions.SectionName).Bind(options);
                // If the Garnet section doesn't specify a connection string,
                // fall back to the Redis connection string.
                if (string.IsNullOrWhiteSpace(options.ConnectionString) ||
                    options.ConnectionString == "127.0.0.1:6379")
                {
                    options.ConnectionString = redisConnection;
                }
            });

            // Singleton multiplexer — shared across all cache consumers
            // (IDistributedCache, SignalR backplane, health checks).
            services.AddSingleton<GarnetConnectionMultiplexer>();

            // Custom IDistributedCache with direct IDatabase access and metrics.
            // Replaces the default RedisCache which creates its own multiplexer.
            services.AddSingleton<IDistributedCache, GarnetDistributedCache>();
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        return services;
    }

    /// <summary>
    /// Output caching for read-heavy endpoints.
    /// No global base policy — only public, genuinely cacheable endpoints
    /// should opt in via .CacheOutput("PolicyName"). A global policy applied before
    /// authentication could serve one user's data to another.
    /// </summary>
    private static IServiceCollection AddOutputCaching(this IServiceCollection services)
    {
        services.AddOutputCache(options =>
        {
            // Branches and loan products rarely change — long TTL.
            options.AddPolicy("BranchCache", builder => builder.Expire(TimeSpan.FromMinutes(5)));
            options.AddPolicy("LoanProductCache", builder => builder.Expire(TimeSpan.FromMinutes(10)));
            options.AddPolicy("DashboardCache", builder => builder.Expire(TimeSpan.FromSeconds(30)));
        });

        return services;
    }
}

namespace EBI.ALAS.Api.Common.Extensions;

/// <summary>
/// Caching configuration (Redis distributed cache + output cache).
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
    /// Configures Redis distributed cache for multi-pod deployments.
    /// Falls back to in-memory if Redis is not configured (dev/single-pod).
    /// </summary>
    private static IServiceCollection AddDistributedCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis");

        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection;
                options.InstanceName = "ALAS_";
            });
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

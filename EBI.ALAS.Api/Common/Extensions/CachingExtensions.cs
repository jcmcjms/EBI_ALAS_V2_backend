using EBI.ALAS.Api.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
namespace EBI.ALAS.Api.Common.Extensions;
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
    private static IServiceCollection AddDistributedCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.Configure<GarnetOptions>(options =>
            {
                configuration.GetSection(GarnetOptions.SectionName).Bind(options);
                if (string.IsNullOrWhiteSpace(options.ConnectionString) ||
                    options.ConnectionString == "127.0.0.1:6379")
                {
                    options.ConnectionString = redisConnection;
                }
            });
            services.AddSingleton<GarnetConnectionMultiplexer>();
            services.AddSingleton<IDistributedCache, GarnetDistributedCache>();
        }
        else
        {
            services.AddDistributedMemoryCache();
        }
        return services;
    }
    private static IServiceCollection AddOutputCaching(this IServiceCollection services)
    {
        services.AddOutputCache(options =>
        {
            options.AddPolicy("BranchCache", builder => builder.Expire(TimeSpan.FromMinutes(5)));
            options.AddPolicy("LoanProductCache", builder => builder.Expire(TimeSpan.FromMinutes(10)));
            options.AddPolicy("DashboardCache", builder => builder.Expire(TimeSpan.FromSeconds(30)));
        });
        return services;
    }
}

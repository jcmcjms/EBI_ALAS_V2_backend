using EBI.ALAS.Api.Infrastructure.Data;
using EBI.ALAS.Api.Infrastructure.Interceptors;
using Microsoft.EntityFrameworkCore;
namespace EBI.ALAS.Api.Common.Extensions;
public static class DatabaseExtensions
{
    public static IServiceCollection AddAppDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sqlOptions =>
                {
                    sqlOptions.CommandTimeout(30);
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorNumbersToAdd: [4060, 40197, 40501, 40613, 49918, 49919, 49920]);
                    sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                });
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });
        return services;
    }
    public static IServiceCollection AddWebLoanDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContextFactory<WebLoanDbContext>(options =>
        {
            options.UseSqlServer(
                configuration.GetConnectionString("WebLoanConnection"),
                sqlOptions =>
                {
                    sqlOptions.CommandTimeout(60);
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorNumbersToAdd: [4060, 40197, 40501, 40613, 49918, 49919, 49920]);
                });
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            options.AddInterceptors(new WebLoanReadOnlyInterceptor());
        });
        return services;
    }
}

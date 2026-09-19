using EBI.ALAS.Api.Infrastructure.Data;
using EBI.ALAS.Api.Infrastructure.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Common.Extensions;

/// <summary>
/// Database context configuration (AppDbContext + WebLoanDbContext).
/// Extracted from Program.cs to follow Single Responsibility Principle.
/// </summary>
public static class DatabaseExtensions
{
    /// <summary>
    /// Primary read-write database context with audit interceptors.
    /// Uses SQL Server 2019 with split queries and retry logic.
    /// </summary>
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

    /// <summary>
    /// Read-only legacy database context (WebLoan).
    /// Uses IDbContextFactory for thread-safe parallel queries.
    /// </summary>
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

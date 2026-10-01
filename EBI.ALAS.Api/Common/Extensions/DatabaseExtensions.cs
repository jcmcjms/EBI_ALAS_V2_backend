using EBI.ALAS.Api.Infrastructure.Data;
using EBI.ALAS.Api.Infrastructure.Interceptors;
using Microsoft.EntityFrameworkCore;
namespace EBI.ALAS.Api.Common.Extensions;

/// <summary>
/// Scoped-compatible IDbContextFactory that resolves the context from the current DI scope.
/// Avoids the singleton-vs-scoped mismatch when AddDbContext is used alongside AddDbContextFactory.
/// </summary>
internal sealed class ScopedDbContextFactory<TContext> : IDbContextFactory<TContext>
    where TContext : DbContext
{
    private readonly IServiceProvider _sp;
    public ScopedDbContextFactory(IServiceProvider sp) => _sp = sp;
    public TContext CreateDbContext() => ActivatorUtilities.CreateInstance<TContext>(_sp);
    public Task<TContext> CreateDbContextAsync(CancellationToken ct = default) =>
        Task.FromResult(CreateDbContext());
}

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
        services.AddScoped<IDbContextFactory<AppDbContext>>(sp =>
            new ScopedDbContextFactory<AppDbContext>(sp));
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

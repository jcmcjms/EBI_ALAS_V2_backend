using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Infrastructure.Background;

public sealed class TokenCleanupHostedService(
    IServiceProvider serviceProvider,
    TimeProvider timeProvider,
    ILogger<TokenCleanupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(60));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = serviceProvider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AlasDbContext>();
                var now = timeProvider.GetUtcNow();
                var expired = await db.RefreshTokens
                    .Where(t => t.ExpiresAt < now || t.AbsoluteExpiresAt < now)
                    .Take(500)
                    .ToListAsync(stoppingToken);
                db.RefreshTokens.RemoveRange(expired);

                var staleJti = await db.RevokedTokens
                    .Where(t => t.ExpiresAt < now)
                    .Take(500)
                    .ToListAsync(stoppingToken);
                db.RevokedTokens.RemoveRange(staleJti);

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Token cleanup failed");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

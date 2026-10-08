using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Infrastructure.Background;

public sealed class QueueReconciliationHostedService(
    IServiceProvider serviceProvider,
    TimeProvider timeProvider,
    ILogger<QueueReconciliationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = serviceProvider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AlasDbContext>();
                var stages = new[] { WorkflowStage.Recommendation, WorkflowStage.Evaluation, WorkflowStage.Approval };
                foreach (var stage in stages)
                {
                    var items = await db.WorkflowQueueItems
                        .Where(q => q.Stage == stage && q.State == QueueItemState.Active)
                        .ToListAsync(stoppingToken);
                    var now = timeProvider.GetUtcNow();
                    foreach (var item in items)
                    {
                        item.ReapExpiredLease(now);
                    }
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Queue reconciliation failed");
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

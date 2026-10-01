using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using EBI.ALAS.Api.Infrastructure.Data;
namespace EBI.ALAS.Api.Features.Loans;
public sealed class QueueReconciliationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<QueueReconciliationHostedService> _logger;
    private readonly IOptionsMonitor<QueueOptions> _queueOptions;
    public QueueReconciliationHostedService(
        IServiceScopeFactory scopes,
        ILogger<QueueReconciliationHostedService> logger,
        IOptionsMonitor<QueueOptions> queueOptions)
    {
        _scopes = scopes;
        _logger = logger;
        _queueOptions = queueOptions;
    }
    private const int ReconciliationBatchLimit = 200;
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMinutes(5);
        _logger.LogInformation(
            "QueueReconciliationHostedService started. Interval: {IntervalMinutes} minutes.",
            interval.TotalMinutes);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var queueService = scope.ServiceProvider.GetRequiredService<IWorkflowQueueService>();
                var staleCount = await db.WorkflowQueueItems
                    .Where(i => (i.State == QueueItemState.Queued || i.State == QueueItemState.Active)
                        && ((i.Stage == QueueStage.Recommendation && i.LoanApplication.Status != "ForRecommendation")
                         || (i.Stage == QueueStage.Evaluation && i.LoanApplication.Status != "ForChecking")
                         || (i.Stage == QueueStage.Approval && i.LoanApplication.Status != "ForApproval")))
                    .Take(ReconciliationBatchLimit)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(i => i.State, QueueItemState.Completed)
                        .SetProperty(i => i.DequeuedAt, DateTime.UtcNow), ct);
                var missingQueue = await db.LoanApplications.AsNoTracking()
                    .Where(l => l.Status == "ForApproval" && l.RequiredApprovalTier != null
                             && !db.WorkflowQueueItems.Any(i =>
                                 i.LoanApplicationId == l.Id
                                 && (i.State == QueueItemState.Active || i.State == QueueItemState.Queued)))
                    .Take(ReconciliationBatchLimit)
                    .ToListAsync(ct);

                if (missingQueue.Count > 0)
                {
                    var now = DateTime.UtcNow;
                    var newItems = missingQueue.Select(loan =>
                    {
                        _logger.LogInformation(
                            "Rule A: enqueueing ForApproval loan {LoanId} (LamId={LamId}, tier={Tier}) — no active queue row.",
                            loan.Id, loan.LamId, loan.RequiredApprovalTier);
                        return new WorkflowQueueItem
                        {
                            LoanApplicationId = loan.Id,
                            Stage = QueueStage.Approval,
                            PartitionKey = $"APP:{loan.BranchCode}:{loan.RequiredApprovalTier}",
                            EnqueuedAt = now,
                            State = QueueItemState.Queued,
                        };
                    }).ToList();

                    db.WorkflowQueueItems.AddRange(newItems);
                }
                var stalePartitions = await db.WorkflowQueueItems
                    .Include(i => i.LoanApplication)
                    .Where(i => (i.State == QueueItemState.Active || i.State == QueueItemState.Queued)
                             && i.Stage == QueueStage.Approval)
                    .ToListAsync(ct);
                var mismatched = stalePartitions
                    .Where(i => i.LoanApplication.RequiredApprovalTier != null
                             && i.PartitionKey != $"APP:{i.LoanApplication.BranchCode}:{i.LoanApplication.RequiredApprovalTier}")
                    .ToList();
                foreach (var item in mismatched)
                {
                    var correctKey = $"APP:{item.LoanApplication.BranchCode}:{item.LoanApplication.RequiredApprovalTier}";
                    _logger.LogInformation(
                        "Rule B: repartitioning queue item {ItemId} for loan {LoanId} from {Old} to {New}.",
                        item.Id, item.LoanApplicationId, item.PartitionKey, correctKey);
                    item.PartitionKey = correctKey;
                }
                if (missingQueue.Count > 0 || mismatched.Count > 0)
                    await db.SaveChangesAsync(ct);
                var stealBefore = DateTime.UtcNow.AddMinutes(-_queueOptions.CurrentValue.LeaseTtlMinutes);
                var expiredCount = await db.WorkflowQueueItems
                    .Where(i => i.State == QueueItemState.Active
                        && i.OwnerUserId != null
                        && i.LeasedAt <= stealBefore)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(i => i.OwnerUserId, (int?)null)
                        .SetProperty(i => i.LeasedAt, (DateTime?)null), ct);
                var partitionsNeedingPromotion = await db.WorkflowQueueItems
                    .Where(i => i.State == QueueItemState.Queued)
                    .Select(i => i.PartitionKey)
                    .Distinct()
                    .Where(pk => !db.WorkflowQueueItems.Any(i =>
                        i.PartitionKey == pk && i.State == QueueItemState.Active))
                    .ToListAsync(ct);
                foreach (var partitionKey in partitionsNeedingPromotion)
                {
                    await queueService.PromoteHeadAsync(partitionKey, ct);
                }
                if (staleCount > 0 || expiredCount > 0 || partitionsNeedingPromotion.Count > 0
                    || missingQueue.Count > 0 || mismatched.Count > 0)
                {
                    _logger.LogInformation(
                        "Queue reconciliation: {StaleCount} stale dequeued, {ExpiredCount} expired leases cleared, " +
                        "{PromotionCount} partitions awaiting promotion, {MissingCount} missing queue rows enqueued, " +
                        "{MismatchCount} stale partitions repartitioned.",
                        staleCount, expiredCount, partitionsNeedingPromotion.Count,
                        missingQueue.Count, mismatched.Count);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Queue reconciliation cycle failed; retrying next interval.");
            }
            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}

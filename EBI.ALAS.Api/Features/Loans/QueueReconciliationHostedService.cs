using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using EBI.ALAS.Api.Infrastructure.Data;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Self-healing sweeper for the workflow queue. Runs every 5 minutes to:
/// 1. Promote the head of any partition that has Queued items but no Active item.
/// 2. Dequeue any live item whose loan status no longer matches its stage.
/// 3. Clear expired leases (OwnerUserId + LeasedAt) so abandoned desks recover.
///
/// This makes the queue resilient to code paths that change status without
/// going through the choke point (e.g. admin bulk operations, future endpoints).
/// </summary>
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

                // 1. Dequeue stale items (loan status no longer matches stage)
                // Note: ForIncompleteDocuments is a tracking state with no queue row.
                // After the cleanup script (release_doc_queue_rows.sql) runs, no
                // DocumentCompletion items should exist. The stale-item check below
                // covers the three real desks only.
                var staleItems = await db.WorkflowQueueItems
                    .Include(i => i.LoanApplication)
                    .Where(i => i.State == QueueItemState.Queued || i.State == QueueItemState.Active)
                    .Where(i => (i.Stage == QueueStage.Recommendation && i.LoanApplication.Status != "ForRecommendation")
                             || (i.Stage == QueueStage.Evaluation && i.LoanApplication.Status != "ForChecking")
                             || (i.Stage == QueueStage.Approval && i.LoanApplication.Status != "ForApproval"))
                    .ToListAsync(ct);

                foreach (var item in staleItems)
                {
                    item.State = QueueItemState.Completed;
                    item.DequeuedAt = DateTime.UtcNow;
                    _logger.LogInformation(
                        "Reconciled stale queue item {ItemId} for loan {LoanId} (stage={Stage}, status={Status}).",
                        item.Id, item.LoanApplicationId, item.Stage, item.LoanApplication.Status);
                }

                if (staleItems.Count > 0)
                    await db.SaveChangesAsync(ct);

                // 1b. Rule A — ForApproval loan with tier but no active queue row → enqueue.
                // Catches loans that were promoted to ForApproval before the routing fix
                // deployed (or via a code path that bypassed the queue).
                var missingQueue = await db.LoanApplications.AsNoTracking()
                    .Where(l => l.Status == "ForApproval" && l.RequiredApprovalTier != null
                             && !db.WorkflowQueueItems.Any(i =>
                                 i.LoanApplicationId == l.Id
                                 && (i.State == QueueItemState.Active || i.State == QueueItemState.Queued)))
                    .ToListAsync(ct);

                foreach (var loan in missingQueue)
                {
                    _logger.LogInformation(
                        "Rule A: enqueueing ForApproval loan {LoanId} (LamId={LamId}, tier={Tier}) — no active queue row.",
                        loan.Id, loan.LamId, loan.RequiredApprovalTier);
                    await queueService.EnqueueAsync(loan, loan.Status, ct);
                }

                // 1c. Rule B — active/queued APP row whose partition disagrees with the
                // loan's current branch/tier (pre-fix rows) → repartition in place.
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

                // 3. Clear expired leases — abandoned desks recover automatically.
                // A reviewer who crashes or walks away doesn't hold a desk hostage.
                var stealBefore = DateTime.UtcNow.AddMinutes(-_queueOptions.CurrentValue.LeaseTtlMinutes);
                var expiredLeases = await db.WorkflowQueueItems
                    .Where(i => i.State == QueueItemState.Active
                                && i.OwnerUserId != null
                                && i.LeasedAt <= stealBefore)
                    .ToListAsync(ct);

                foreach (var item in expiredLeases)
                {
                    _logger.LogInformation(
                        "Clearing expired lease on queue item {ItemId} for loan {LoanId} " +
                        "(owner={OwnerUserId}, leased at {LeasedAt}, ttl={TtlMinutes}min).",
                        item.Id, item.LoanApplicationId, item.OwnerUserId, item.LeasedAt,
                        _queueOptions.CurrentValue.LeaseTtlMinutes);
                    item.OwnerUserId = null;
                    item.LeasedAt = null;
                }

                if (expiredLeases.Count > 0)
                    await db.SaveChangesAsync(ct);

                // 2. Promote heads of partitions that have no Active item
                // Find partitions with Queued items but no Active item.
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

                if (staleItems.Count > 0 || expiredLeases.Count > 0 || partitionsNeedingPromotion.Count > 0
                    || missingQueue.Count > 0 || mismatched.Count > 0)
                {
                    _logger.LogInformation(
                        "Queue reconciliation: {StaleCount} stale dequeued, {ExpiredCount} expired leases cleared, " +
                        "{PromotionCount} partitions awaiting promotion, {MissingCount} missing queue rows enqueued, " +
                        "{MismatchCount} stale partitions repartitioned.",
                        staleItems.Count, expiredLeases.Count, partitionsNeedingPromotion.Count,
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

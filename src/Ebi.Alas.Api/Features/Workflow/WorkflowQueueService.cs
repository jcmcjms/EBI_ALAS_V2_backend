using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Workflow;

public sealed class WorkflowQueueService(AlasDbContext db, TimeProvider timeProvider)
{
    public static readonly TimeSpan DefaultLease = TimeSpan.FromMinutes(30);
    public const int ReapBatchSize = 100;

    public static string ResolvePartition(CallerContext caller, string? requestedBranchId)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (caller.CanAccessAllBranches && !string.IsNullOrWhiteSpace(requestedBranchId))
        {
            return requestedBranchId.Trim();
        }

        return caller.BranchId;
    }

    public async Task<WorkflowQueueItem> EnqueueAsync(
        Guid loanApplicationId,
        WorkflowStage stage,
        string partitionKey,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var maxSequence = await db.WorkflowQueueItems
            .Where(q => q.Stage == stage && q.PartitionKey == partitionKey)
            .Select(q => (int?)q.Sequence)
            .MaxAsync(cancellationToken) ?? 0;

        var item = WorkflowQueueItem.Enqueue(
            loanApplicationId,
            stage,
            partitionKey,
            maxSequence + 1,
            DefaultLease,
            now);
        db.WorkflowQueueItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task<WorkflowQueueItem> ClaimNextAsync(
        WorkflowStage stage,
        string partitionKey,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await ReapExpiredAsync(stage, partitionKey, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var candidates = await db.WorkflowQueueItems
            .Where(q => q.Stage == stage
                && q.PartitionKey == partitionKey
                && q.State == QueueItemState.Queued)
            .OrderBy(q => q.Sequence)
            .Take(20)
            .ToListAsync(cancellationToken);

        var head = candidates.FirstOrDefault();
        if (head is null)
        {
            throw new NotFoundException("Queue item", $"{stage}/{partitionKey}");
        }

        if (!head.TryClaim(userId, now))
        {
            throw new ConflictException("Queue item is already claimed.");
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Queue item was claimed concurrently.");
        }

        return head;
    }

    public async Task ReleaseAsync(Guid itemId, Guid userId, CancellationToken cancellationToken)
    {
        var item = await db.WorkflowQueueItems.FirstOrDefaultAsync(q => q.Id == itemId, cancellationToken)
            ?? throw new NotFoundException("Queue item", itemId.ToString());
        if (!item.Release(userId, timeProvider.GetUtcNow()))
        {
            throw new ForbiddenException("Only the lease owner can release the item.");
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(Guid itemId, Guid userId, CancellationToken cancellationToken)
    {
        if (itemId == Guid.Empty)
        {
            return;
        }

        var item = await db.WorkflowQueueItems.FirstOrDefaultAsync(q => q.Id == itemId, cancellationToken)
            ?? throw new NotFoundException("Queue item", itemId.ToString());
        item.Complete(userId, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteOpenForApplicationAsync(
        Guid loanApplicationId,
        CancellationToken cancellationToken)
    {
        var items = await db.WorkflowQueueItems
            .Where(q => q.LoanApplicationId == loanApplicationId
                && (q.State == QueueItemState.Queued || q.State == QueueItemState.Active))
            .ToListAsync(cancellationToken);
        if (items.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var item in items)
        {
            item.ForceComplete(now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ReapExpiredAsync(
        WorkflowStage stage,
        string partitionKey,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var items = await db.WorkflowQueueItems
            .Where(q => q.Stage == stage && q.PartitionKey == partitionKey)
            .Take(ReapBatchSize)
            .ToListAsync(cancellationToken);
        var count = 0;
        foreach (var item in items)
        {
            if (item.IsLeaseExpired(now))
            {
                item.ReapExpiredLease(now);
                count++;
            }
        }

        if (count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return count;
    }

    public async Task<IReadOnlyList<WorkflowQueueItem>> ListAsync(
        WorkflowStage stage,
        string partitionKey,
        int max,
        CancellationToken cancellationToken)
    {
        return await db.WorkflowQueueItems.AsNoTracking()
            .Where(q => q.Stage == stage && q.PartitionKey == partitionKey)
            .OrderBy(q => q.Sequence)
            .Take(max)
            .ToListAsync(cancellationToken);
    }
}

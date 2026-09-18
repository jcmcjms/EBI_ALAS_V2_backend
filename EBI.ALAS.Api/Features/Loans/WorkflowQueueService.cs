using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

public class WorkflowQueueService : IWorkflowQueueService
{
    private readonly AppDbContext _db;
    private readonly ILoanRepository _loanRepo;
    private readonly INotificationService _notifications;
    private readonly IRealtimeNotificationService _realtime;
    private readonly ITimeProvider _time;

    public WorkflowQueueService(
        AppDbContext db,
        ILoanRepository loanRepo,
        INotificationService notifications,
        IRealtimeNotificationService realtime,
        ITimeProvider time)
    {
        _db = db;
        _loanRepo = loanRepo;
        _notifications = notifications;
        _realtime = realtime;
        _time = time;
    }

    /// <summary>
    /// Maps a workflow status to its corresponding queue stage.
    /// Returns null for statuses that don't occupy a review desk.
    /// </summary>
    public static QueueStage? StageForStatus(string status) => status switch
    {
        "ForRecommendation" => QueueStage.Recommendation,
        "ForChecking" => QueueStage.Evaluation,
        "ForApproval" => QueueStage.Approval,
        _ => null,
    };

    /// <summary>
    /// Builds the partition key for a given stage and loan.
    /// Recommendation/Evaluation: "REC:{branchCode}" / "EVA:{branchCode}"
    /// Approval: "APP:{branchCode}:{tier}"
    /// </summary>
    private static string PartitionKey(QueueStage stage, LoanApplication loan) => stage switch
    {
        QueueStage.Approval => $"APP:{loan.BranchCode}:{loan.RequiredApprovalTier ?? 0}",
        _ => $"{stage.ToString()[..3].ToUpperInvariant()}:{loan.BranchCode}",
    };

    public async Task EnqueueAsync(LoanApplication loan, string newStatus, CancellationToken ct)
    {
        var stage = StageForStatus(newStatus);
        if (stage == null) return;

        var partitionKey = PartitionKey(stage.Value, loan);

        _db.WorkflowQueueItems.Add(new WorkflowQueueItem
        {
            LoanApplicationId = loan.Id,
            Stage = stage.Value,
            PartitionKey = partitionKey,
            EnqueuedAt = _time.UtcNow,
            State = QueueItemState.Queued,
        });
        await _db.SaveChangesAsync(ct);

        await PromoteAsync(partitionKey, loan, ct);
    }

    public async Task DequeueAndPromoteAsync(LoanApplication loan, string oldStatus, CancellationToken ct)
    {
        var stage = StageForStatus(oldStatus);
        if (stage == null) return;

        var item = await _db.WorkflowQueueItems.FirstOrDefaultAsync(i =>
            i.LoanApplicationId == loan.Id && i.Stage == stage &&
            (i.State == QueueItemState.Queued || i.State == QueueItemState.Active), ct);
        if (item == null) return;

        var partitionKey = item.PartitionKey;

        item.State = QueueItemState.Completed;
        item.DequeuedAt = _time.UtcNow;
        await _db.SaveChangesAsync(ct);

        await PromoteAsync(partitionKey, loan, ct);
    }

    /// <summary>
    /// Head = oldest live item. If the desk is free, promote and materialize owner.
    /// </summary>
    private async Task PromoteAsync(string partitionKey, LoanApplication loan, CancellationToken ct)
    {
        // Desk busy — new item waits its turn
        if (await _db.WorkflowQueueItems.AnyAsync(i =>
                i.PartitionKey == partitionKey && i.State == QueueItemState.Active, ct))
            return;

        var head = await _db.WorkflowQueueItems
            .Where(i => i.PartitionKey == partitionKey && i.State == QueueItemState.Queued)
            .OrderBy(i => i.EnqueuedAt).ThenBy(i => i.Id)
            .FirstOrDefaultAsync(ct);
        if (head == null) return;

        var owner = await ResolveOwnerAsync(head.Stage, loan, ct);
        head.State = QueueItemState.Active;
        head.PromotedAt = _time.UtcNow;
        head.OwnerUserId = owner?.Id;

        // Keep the delegation-of-authority contract intact for approval prints/guards.
        if (head.Stage == QueueStage.Approval)
        {
            var application = await _db.LoanApplications.FindAsync([loan.Id], ct);
            if (application != null)
            {
                application.AssignedApproverId = owner?.Id;
                application.AssignedAt = owner == null ? null : _time.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);

        if (owner != null)
        {
            var link = $"/loans/monitoring?id={loan.Id}";
            var title = "Your turn: application ready for review";
            var body = $"{loan.LamId} ({loan.FirstName} {loan.LastName}) is next in your queue.";
            await _notifications.CreateAsync(owner.Id, title, body, link);
            await _realtime.NotifyUserAsync(owner.Id, title, body, link);
        }
    }

    /// <summary>
    /// Deterministic: least live items owned, then lowest Id.
    /// Zero users → unowned desk.
    /// </summary>
    private async Task<User?> ResolveOwnerAsync(QueueStage stage, LoanApplication loan, CancellationToken ct)
    {
        var role = stage switch
        {
            QueueStage.Recommendation => Roles.Recommender,
            QueueStage.Evaluation => Roles.Evaluator,
            _ => Roles.Approver,
        };

        var candidates = await _loanRepo.GetUsersByRoleAndBranchAsync(role, loan.BranchCode, ct);

        if (stage == QueueStage.Approval && loan.RequiredApprovalTier is int tier)
        {
            var keys = await _db.ApprovalAuthorities
                .Where(a => a.Tier == tier).Select(a => a.Key).ToListAsync(ct);
            candidates = candidates
                .Where(u => u.ApprovalAuthorityKey != null && keys.Contains(u.ApprovalAuthorityKey))
                .ToList();
        }

        if (candidates.Count == 0) return null;

        var candidateIds = candidates.Select(u => u.Id).ToList();
        var load = await _db.WorkflowQueueItems
            .Where(i => i.State == QueueItemState.Active && i.OwnerUserId != null
                        && candidateIds.Contains(i.OwnerUserId.Value))
            .GroupBy(i => i.OwnerUserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        return candidates
            .OrderBy(u => load.GetValueOrDefault(u.Id))
            .ThenBy(u => u.Id)
            .First();
    }

    public async Task<IReadOnlyDictionary<int, QueuePositionInfo>> GetPositionsAsync(
        IReadOnlyCollection<int> loanIds, CancellationToken ct)
    {
        if (loanIds.Count == 0) return new Dictionary<int, QueuePositionInfo>();

        // Live items for this page + their whole partitions (desks are small;
        // rank is computed in memory to avoid window functions in the list query).
        var mine = await _db.WorkflowQueueItems.AsNoTracking()
            .Where(i => loanIds.Contains(i.LoanApplicationId) && i.State != QueueItemState.Completed)
            .ToListAsync(ct);
        if (mine.Count == 0) return new Dictionary<int, QueuePositionInfo>();

        var partitions = mine.Select(i => i.PartitionKey).Distinct().ToList();
        var partitionItems = await _db.WorkflowQueueItems.AsNoTracking()
            .Include(i => i.OwnerUser)
            .Where(i => partitions.Contains(i.PartitionKey) && i.State != QueueItemState.Completed)
            .OrderBy(i => i.PartitionKey).ThenBy(i => i.EnqueuedAt).ThenBy(i => i.Id)
            .ToListAsync(ct);

        var result = new Dictionary<int, QueuePositionInfo>();
        foreach (var group in partitionItems.GroupBy(i => i.PartitionKey))
        {
            var rank = 0;
            foreach (var item in group)
            {
                rank++;
                if (!loanIds.Contains(item.LoanApplicationId)) continue;
                var ownerName = item.OwnerUser == null ? null
                    : $"{item.OwnerUser.FirstName} {item.OwnerUser.LastName}";
                result[item.LoanApplicationId] = new QueuePositionInfo(
                    item.Stage, rank, 0, item.OwnerUserId, ownerName, rank == 1);
            }
            // Second pass to stamp total length on the rows we return.
            foreach (var id in result.Keys.Where(id =>
                         group.Any(i => i.LoanApplicationId == id)).ToList())
            {
                result[id] = result[id] with { QueueLength = rank };
            }
        }
        return result;
    }

    public async Task<bool> IsHeadOwnerAsync(int loanId, int userId, string currentStatus, CancellationToken ct)
    {
        var positions = await GetPositionsAsync([loanId], ct);
        return positions.TryGetValue(loanId, out var info)
            && info.IsHead && info.OwnerUserId == userId;
    }
}

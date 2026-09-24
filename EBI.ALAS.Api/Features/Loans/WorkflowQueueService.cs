using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EBI.ALAS.Api.Features.Loans;

public class WorkflowQueueService : IWorkflowQueueService
{
    private readonly AppDbContext _db;
    private readonly ILoanRepository _loanRepo;
    private readonly INotificationService _notifications;
    private readonly IRealtimeNotificationService _realtime;
    private readonly ITimeProvider _time;
    private readonly IOptionsMonitor<QueueOptions> _queueOptions;

    public WorkflowQueueService(
        AppDbContext db,
        ILoanRepository loanRepo,
        INotificationService notifications,
        IRealtimeNotificationService realtime,
        ITimeProvider time,
        IOptionsMonitor<QueueOptions> queueOptions)
    {
        _db = db;
        _loanRepo = loanRepo;
        _notifications = notifications;
        _realtime = realtime;
        _time = time;
        _queueOptions = queueOptions;
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
        // ForIncompleteDocuments is a tracking state, not a turn-based desk.
        // Document completion is parallel work (no head owner); reviewers may
        // route flagged files at any time. Returning null keeps the ownership
        // guard and FIFO promotion out of the flagged state entirely.
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
        // Replaced the O(partition-size) GetPositionsAsync call with a
        // targeted top-1 query. The old code loaded EVERY non-completed queue item
        // in the partition + a join to Users, then ranked in memory. A busy desk
        // with 1,000 queued files pulled 1,000 rows on each status change.
        // This query checks directly: is the given loan the head (oldest active/queued)
        // in its partition, and is it owned by the given user?
        var stage = StageForStatus(currentStatus);
        if (stage == null) return true; // No queue for this status — allow

        var partitionKey = PartitionKey(stage.Value, new LoanApplication
        {
            BranchCode = "", // We need the actual branch code — get it from the loan
        });

        // We need the loan's branch code to build the partition key.
        // Look it up from the queue item directly.
        var queueItem = await _db.WorkflowQueueItems
            .AsNoTracking()
            .Where(i => i.LoanApplicationId == loanId
                        && i.Stage == stage.Value
                        && (i.State == QueueItemState.Active || i.State == QueueItemState.Queued))
            .Select(i => new { i.PartitionKey, i.OwnerUserId, i.State, i.EnqueuedAt, i.Id })
            .FirstOrDefaultAsync(ct);

        if (queueItem is null) return true; // Loan not in queue — allow (admin/edge case)

        // If the loan is already active and owned by this user, it's their turn
        if (queueItem.State == QueueItemState.Active && queueItem.OwnerUserId == userId)
            return true;

        var headItem = await _db.WorkflowQueueItems
            .AsNoTracking()
            .Where(i => i.PartitionKey == queueItem.PartitionKey
                        && (i.State == QueueItemState.Active || i.State == QueueItemState.Queued))
            .OrderBy(i => i.State == QueueItemState.Active ? 0 : 1) // Active first
            .ThenBy(i => i.EnqueuedAt)
            .ThenBy(i => i.Id)
            .Select(i => new { i.LoanApplicationId, i.OwnerUserId, i.State })
            .FirstOrDefaultAsync(ct);

        return headItem is not null
               && headItem.LoanApplicationId == loanId
               && headItem.OwnerUserId == userId;
    }

    // ── Review Desk: atomic head-lease ──────────────────────────────────

    /// <summary>
    /// Maps a role + branch code to the set of partition keys the reviewer
    /// can claim from. Recommender → REC:{bch}, Evaluator → EVA:{bch},
    /// Approver → all APP:{bch}:{tier} the approval matrix authorizes.
    /// </summary>
    private async Task<List<string>> DeskPartitionsAsync(string role, string branchCode, CancellationToken ct)
    {
        return role switch
        {
            Roles.Recommender => [$"REC:{branchCode}"],
            Roles.Evaluator => [$"EVA:{branchCode}"],
            Roles.Approver =>
            [
                ..(await _db.ApprovalAuthorities.AsNoTracking()
                    .Select(a => a.Tier)
                    .Distinct()
                    .ToListAsync(ct))
                    .Select(tier => $"APP:{branchCode}:{tier}")
            ],
            _ => [],
        };
    }

    /// <summary>
    /// Human-readable desk label for the UI.
    /// </summary>
    private static string DeskLabelFor(string role) => role switch
    {
        Roles.Recommender => "Recommendation",
        Roles.Evaluator => "Evaluation",
        Roles.Approver => "Approval",
        _ => "Review",
    };

    public async Task<ClaimResponse?> ClaimHeadAsync(
        int userId, string role, string branchCode, CancellationToken ct)
    {
        var prefixes = await DeskPartitionsAsync(role, branchCode, ct);
        if (prefixes.Count == 0) return null;

        var now = _time.UtcNow;
        var stealBefore = now.AddMinutes(-_queueOptions.CurrentValue.LeaseTtlMinutes);

        // Bounded optimistic-concurrency loop: pick the FIFO candidate, then
        // win it with a conditional UPDATE. Losers retry on the next head;
        // after 3 misses the desk is genuinely busy → null, never a double-lease.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var candidate = await _db.WorkflowQueueItems.AsNoTracking()
                .Where(i => i.State == QueueItemState.Active
                            && prefixes.Contains(i.PartitionKey)
                            && (i.OwnerUserId == null
                                || i.OwnerUserId == userId
                                || i.LeasedAt <= stealBefore))
                .OrderBy(i => i.EnqueuedAt).ThenBy(i => i.Id)
                .Select(i => new { i.Id, i.LoanApplicationId, i.PartitionKey })
                .FirstOrDefaultAsync(ct);

            if (candidate is null) return null;

            var won = await _db.WorkflowQueueItems
                .Where(i => i.Id == candidate.Id
                            && i.State == QueueItemState.Active
                            && (i.OwnerUserId == null
                                || i.OwnerUserId == userId
                                || i.LeasedAt <= stealBefore))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.OwnerUserId, userId)
                    .SetProperty(i => i.LeasedAt, now), ct);

            if (won == 1)
            {
                // Load the loan for the response DTO
                var loan = await _db.LoanApplications.AsNoTracking()
                    .Where(l => l.Id == candidate.LoanApplicationId)
                    .Select(l => new { l.Id, l.LamId, l.FirstName, l.LastName, l.Status })
                    .FirstOrDefaultAsync(ct);

                if (loan is null) return null;

                var clientName = $"{loan.FirstName} {loan.LastName}".Trim();
                return new ClaimResponse(loan.Id, loan.LamId, clientName, loan.Status, now);
            }
            // Lost the race — retry on the next head
        }

        return null; // contended desk → caller surfaces "queue is busy, retry"
    }

    public async Task<DeskQueueResponse> GetDeskAsync(
        int userId, string role, string branchCode, CancellationToken ct)
    {
        var prefixes = await DeskPartitionsAsync(role, branchCode, ct);
        if (prefixes.Count == 0)
            return new DeskQueueResponse(DeskLabelFor(role), [], null);

        var items = await _db.WorkflowQueueItems.AsNoTracking()
            .Include(i => i.OwnerUser)
            .Include(i => i.LoanApplication)
            .Where(i => prefixes.Contains(i.PartitionKey)
                        && i.State == QueueItemState.Active)
            .OrderBy(i => i.EnqueuedAt).ThenBy(i => i.Id)
            .ToListAsync(ct);

        var rank = 0;
        var dtos = new List<QueuedLoanDto>(items.Count);
        QueuedLoanDto? currentClaim = null;

        foreach (var item in items)
        {
            rank++;
            var ownerName = item.OwnerUser == null
                ? null
                : $"{item.OwnerUser.FirstName} {item.OwnerUser.LastName}";
            var clientName = item.LoanApplication == null
                ? "Unknown"
                : $"{item.LoanApplication.FirstName} {item.LoanApplication.LastName}".Trim();

            var dto = new QueuedLoanDto(
                item.LoanApplicationId,
                item.LoanApplication?.LamId ?? "",
                clientName,
                rank,
                rank == 1,
                item.OwnerUserId,
                ownerName,
                item.EnqueuedAt,
                item.LoanApplication?.Status ?? "");

            dtos.Add(dto);

            if (item.OwnerUserId == userId)
                currentClaim = dto;
        }

        return new DeskQueueResponse(DeskLabelFor(role), dtos, currentClaim);
    }

    public async Task<bool> ReleaseClaimAsync(int userId, CancellationToken ct)
    {
        var item = await _db.WorkflowQueueItems
            .FirstOrDefaultAsync(i =>
                i.OwnerUserId == userId
                && i.State == QueueItemState.Active, ct);

        if (item is null) return false;

        item.OwnerUserId = null;
        item.LeasedAt = null;
        await _db.SaveChangesAsync(ct);

        // Re-promote: the head is now unowned, so the next claim wins it.
        // The existing PromoteAsync handles this, but we need the loan to call it.
        var loan = await _db.LoanApplications.FindAsync([item.LoanApplicationId], ct);
        if (loan is not null)
            await PromoteAsync(item.PartitionKey, loan, ct);

        return true;
    }
}

using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.ApprovalMatrix;
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

        if (stage == QueueStage.Approval && loan.RequiredApprovalTier is null)
            throw new InvalidOperationException(
                $"Cannot enqueue loan {loan.Id} for approval — RequiredApprovalTier is null. " +
                "Route through the approval matrix first.");

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

        int? ownerId = null;
        if (head.Stage == QueueStage.Approval)
        {
            var owner = await ResolveApproverAsync(loan, ct);
            ownerId = owner?.Id;

            var application = await _db.LoanApplications.FindAsync([loan.Id], ct);
            if (application != null)
            {
                application.AssignedApproverId = owner?.Id;
                application.AssignedAt = owner == null ? null : _time.UtcNow;
            }

            if (owner != null)
            {
                var link = $"/loans/monitoring?id={loan.Id}";
                var title = "Your turn: application ready for review";
                var body = $"{loan.LamId} ({loan.FirstName} {loan.LastName}) is next in your queue.";
                await _notifications.CreateAsync(owner.Id, title, body, link);
                await _realtime.NotifyUserAsync(owner.Id, title, body, link);
            }
        }

        head.State = QueueItemState.Active;
        head.PromotedAt = _time.UtcNow;
        head.OwnerUserId = ownerId;
        head.LeasedAt = ownerId is null ? null : _time.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Deterministic: least live items owned, then lowest Id.
    /// Zero candidates → null so the promotion leaves the head unowned.
    /// </summary>
    private async Task<User?> ResolveApproverAsync(LoanApplication loan, CancellationToken ct)
    {
        var candidates = await _loanRepo.GetUsersByRoleAndBranchAsync(Roles.Approver, loan.BranchCode, ct);

        if (loan.RequiredApprovalTier is int tier)
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

    public async Task<LoanQueueState?> GetQueueStateAsync(
        int loanId, int userId, string currentStatus, CancellationToken ct)
    {
        var stage = StageForStatus(currentStatus);
        if (stage is null) return null;

        var partitionKey = await _db.WorkflowQueueItems
            .Where(i => i.LoanApplicationId == loanId
                        && i.Stage == stage.Value
                        && i.State != QueueItemState.Completed)
            .Select(i => i.PartitionKey)
            .FirstOrDefaultAsync(ct);

        if (partitionKey is null) return null;

        var ordered = await _db.WorkflowQueueItems.AsNoTracking()
            .Include(i => i.OwnerUser)
            .Where(i => i.PartitionKey == partitionKey && i.State != QueueItemState.Completed)
            .OrderBy(i => i.EnqueuedAt).ThenBy(i => i.Id)
            .ToListAsync(ct);

        var position = 0;
        foreach (var item in ordered)
        {
            position++;
            if (item.LoanApplicationId != loanId) continue;

            var ownerName = item.OwnerUser is null
                ? null
                : $"{item.OwnerUser.FirstName} {item.OwnerUser.LastName}";

            return new LoanQueueState(
                position,
                position == 1,
                item.OwnerUserId,
                ownerName,
                item.OwnerUserId == userId,
                item.LeasedAt);
        }

        return null;
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
    /// Admin → every active approval partition, Approver → partitions
    /// derived from the user's delegated authority scope (not home branch).
    /// </summary>
    private async Task<List<string>> DeskPartitionsAsync(string role, string branchCode, int userId, CancellationToken ct)
    {
        return role switch
        {
            Roles.Recommender => [$"REC:{branchCode}"],
            Roles.Evaluator => [$"EVA:{branchCode}"],
            Roles.Admin => await _db.WorkflowQueueItems.AsNoTracking()
                .Where(i => i.Stage == QueueStage.Approval)
                .Select(i => i.PartitionKey).Distinct().ToListAsync(ct),
            Roles.Approver => (await ApproverPartitionsAsync(userId, ct)).Keys,
            _ => [],
        };
    }

    /// <summary>
    /// An approver's desk is defined by their delegated authority, not their
    /// home branch: Global sees every branch at their tier, Area sees the
    /// covered branches, Branch sees only home. This is what a Credit Head
    /// (Tier 3, Global) was missing — files from other branches never matched.
    /// </summary>
    private async Task<(List<string> Keys, string Scope)> ApproverPartitionsAsync(int userId, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking()
            .Include(u => u.BranchCoverages)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user?.ApprovalAuthorityKey is null) return ([], "No authority assigned");

        var authority = await _db.ApprovalAuthorities.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Key == user.ApprovalAuthorityKey, ct);
        if (authority is null) return ([], "No authority assigned");

        var branches = authority.ScopeType switch
        {
            AuthorityScope.Global => await _db.Branches.AsNoTracking()
                .Select(b => b.Code).ToListAsync(ct),
            AuthorityScope.Area => user.BranchCoverages.Select(c => c.BranchCode).ToList(),
            _ => [user.BranchId],
        };

        var scope = authority.ScopeType switch
        {
            AuthorityScope.Global => $"Global authority — all {branches.Count} branches",
            AuthorityScope.Area => $"Area authority — {branches.Count} branch{(branches.Count == 1 ? "" : "es")} ({string.Join(", ", branches.Order())})",
            _ => $"Branch {user.BranchId}",
        };

        return (branches.Select(b => $"APP:{b}:{authority.Tier}").ToList(), scope);
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
        var prefixes = await DeskPartitionsAsync(role, branchCode, userId, ct);
        if (prefixes.Count == 0) return null;

        var now = _time.UtcNow;
        var stealBefore = now.AddMinutes(-_queueOptions.CurrentValue.LeaseTtlMinutes);

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

            var (won, loanApplicationId) = await TryLeaseItemAsync(candidate.Id, userId, ct);
            if (!won) continue;

            var loan = await _db.LoanApplications.AsNoTracking()
                .Where(l => l.Id == loanApplicationId)
                .Select(l => new { l.Id, l.LamId, l.FirstName, l.LastName, l.Status })
                .FirstOrDefaultAsync(ct);

            if (loan is null) return null;

            var clientName = $"{loan.FirstName} {loan.LastName}".Trim();
            return new ClaimResponse(loan.Id, loan.LamId, clientName, loan.Status, now);
        }

        return null;
    }

    private async Task<(bool Won, int LoanApplicationId)> TryLeaseItemAsync(
        int itemId, int userId, CancellationToken ct)
    {
        var now = _time.UtcNow;
        var stealBefore = now.AddMinutes(-_queueOptions.CurrentValue.LeaseTtlMinutes);

        var won = await _db.WorkflowQueueItems
            .Where(i => i.Id == itemId
                        && i.State == QueueItemState.Active
                        && (i.OwnerUserId == null
                            || i.OwnerUserId == userId
                            || i.LeasedAt <= stealBefore))
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.OwnerUserId, userId)
                .SetProperty(i => i.LeasedAt, now), ct);

        if (won != 1)
            return (false, 0);

        var loanId = await _db.WorkflowQueueItems
            .Where(i => i.Id == itemId)
            .Select(i => i.LoanApplicationId)
            .FirstAsync(ct);

        return (true, loanId);
    }

    public async Task<DeskQueueResponse> GetDeskAsync(
        int userId, string role, string branchCode, CancellationToken ct)
    {
        List<string> prefixes;
        string scope;

        if (role == Roles.Approver)
        {
            (prefixes, scope) = await ApproverPartitionsAsync(userId, ct);
        }
        else
        {
            prefixes = await DeskPartitionsAsync(role, branchCode, userId, ct);
            scope = role == Roles.Admin ? "All approval partitions" : $"{branchCode} — {DeskLabelFor(role)}";
        }

        if (prefixes.Count == 0)
            return new DeskQueueResponse(DeskLabelFor(role), [], null, scope);

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

        return new DeskQueueResponse(DeskLabelFor(role), dtos, currentClaim, scope);
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

    public async Task<ClaimByIdResult> ClaimByIdAsync(
        int id, int userId, string role, string branchCode, CancellationToken ct)
    {
        var prefixes = await DeskPartitionsAsync(role, branchCode, userId, ct);
        if (prefixes.Count == 0)
            return new ClaimByIdResult.NotFound();

        var item = await _db.WorkflowQueueItems.AsNoTracking()
            .Where(i => i.LoanApplicationId == id
                        && prefixes.Contains(i.PartitionKey)
                        && (i.State == QueueItemState.Active || i.State == QueueItemState.Queued))
            .OrderBy(i => i.EnqueuedAt).ThenBy(i => i.Id)
            .Select(i => new { i.Id, i.OwnerUserId, i.State })
            .FirstOrDefaultAsync(ct);

        if (item is null)
            return new ClaimByIdResult.NotFound();

        if (item.State != QueueItemState.Active)
            return new ClaimByIdResult.NotHead();

        if (item.OwnerUserId is not null && item.OwnerUserId != userId)
        {
            var ownerName = await _db.Users
                .Where(u => u.Id == item.OwnerUserId)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefaultAsync(ct) ?? "another reviewer";

            return new ClaimByIdResult.LeasedByOther(ownerName);
        }

        var (won, loanApplicationId) = await TryLeaseItemAsync(item.Id, userId, ct);
        if (!won)
            return new ClaimByIdResult.LeasedByOther("another reviewer");

        var loan = await _db.LoanApplications.AsNoTracking()
            .Where(l => l.Id == loanApplicationId)
            .Select(l => new { l.Id, l.LamId, l.FirstName, l.LastName, l.Status })
            .FirstOrDefaultAsync(ct);

        if (loan is null)
            return new ClaimByIdResult.NotFound();

        var clientName = $"{loan.FirstName} {loan.LastName}".Trim();
        return new ClaimByIdResult.Claimed(
            new ClaimResponse(loan.Id, loan.LamId, clientName, loan.Status, _time.UtcNow));
    }

    public async Task PromoteHeadAsync(string partitionKey, CancellationToken ct)
    {
        var head = await _db.WorkflowQueueItems
            .Include(i => i.LoanApplication)
            .Where(i => i.PartitionKey == partitionKey && i.State == QueueItemState.Queued)
            .OrderBy(i => i.EnqueuedAt).ThenBy(i => i.Id)
            .FirstOrDefaultAsync(ct);

        if (head?.LoanApplication is null) return;

        await PromoteAsync(partitionKey, head.LoanApplication, ct);
    }
}

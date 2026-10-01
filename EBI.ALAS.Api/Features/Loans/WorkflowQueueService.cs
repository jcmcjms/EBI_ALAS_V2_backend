using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
namespace EBI.ALAS.Api.Features.Loans;
public class WorkflowQueueService : IWorkflowQueueService
{
    private readonly AppDbContext _db;
    private readonly ILoanRepository _loanRepo;
    private readonly INotificationService _notifications;
    private readonly ITimeProvider _time;
    private readonly IOptionsMonitor<QueueOptions> _queueOptions;
    private readonly IMemoryCache _cache;
    public WorkflowQueueService(
        AppDbContext db,
        ILoanRepository loanRepo,
        INotificationService notifications,
        ITimeProvider time,
        IOptionsMonitor<QueueOptions> queueOptions,
        IMemoryCache cache)
    {
        _db = db;
        _loanRepo = loanRepo;
        _notifications = notifications;
        _time = time;
        _queueOptions = queueOptions;
        _cache = cache;
    }
    public static QueueStage? StageForStatus(string status) => status switch
    {
        "ForRecommendation" => QueueStage.Recommendation,
        "ForChecking" => QueueStage.Evaluation,
        "ForApproval" => QueueStage.Approval,
        _ => null,
    };
    public string GetPartitionKey(LoanApplication loan, QueueStage stage) => PartitionKey(stage, loan);
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
    public void TrackEnqueue(LoanApplication loan, string newStatus)
    {
        var stage = StageForStatus(newStatus);
        if (stage is null) return;
        if (stage == QueueStage.Approval && loan.RequiredApprovalTier is null)
            throw new InvalidOperationException(
                $"Cannot enqueue loan {loan.Id} for approval — RequiredApprovalTier is null. " +
                "Route through the approval matrix first.");
        var item = new WorkflowQueueItem
        {
            LoanApplicationId = loan.Id,
            Stage = stage.Value,
            PartitionKey = PartitionKey(stage.Value, loan),
            EnqueuedAt = _time.UtcNow,
            State = QueueItemState.Queued
        };
        _db.WorkflowQueueItems.Add(item);
    }
    public async Task DequeueAndPromoteAsync(LoanApplication loan, string oldStatus, CancellationToken ct)
    {
        await DequeueAndPromoteCoreAsync(loan, oldStatus, save: true, ct);
    }
    public async Task DequeueAndPromoteWithoutSaveAsync(LoanApplication loan, string oldStatus, CancellationToken ct)
    {
        await DequeueAndPromoteCoreAsync(loan, oldStatus, save: false, ct);
    }
    private async Task DequeueAndPromoteCoreAsync(LoanApplication loan, string oldStatus, bool save, CancellationToken ct)
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
        if (save) await _db.SaveChangesAsync(ct);
        await PromoteCoreAsync(partitionKey, loan, save, ct);
    }
    private async Task PromoteAsync(string partitionKey, LoanApplication loan, CancellationToken ct)
    {
        await PromoteCoreAsync(partitionKey, loan, save: true, ct);
    }
    private async Task PromoteCoreAsync(string partitionKey, LoanApplication loan, bool save, CancellationToken ct)
    {
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
                _notifications.TrackCreate(owner.Id, title, body, link);
            }
        }
        head.State = QueueItemState.Active;
        head.PromotedAt = _time.UtcNow;
        head.OwnerUserId = ownerId;
        head.LeasedAt = ownerId is null ? null : _time.UtcNow;
        if (save) await _db.SaveChangesAsync(ct);
    }
    private async Task<User?> ResolveApproverAsync(LoanApplication loan, CancellationToken ct)
    {
        var candidates = await _loanRepo.GetUsersByRoleAndBranchAsync(Roles.Approver, loan.BranchCode, ct);
        if (loan.RequiredApprovalTier is int tier)
        {
            var tierCacheKey = $"approval_tier:{tier}";
            if (!_cache.TryGetValue(tierCacheKey, out List<string>? keys) || keys is null)
            {
                keys = await _db.ApprovalAuthorities
                    .Where(a => a.Tier == tier).Select(a => a.Key).ToListAsync(ct);
                _cache.Set(tierCacheKey, keys, TimeSpan.FromMinutes(5));
            }
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
        // State stored as string via HasConversion<string>() in WorkflowQueueItemConfiguration
        var rows = await _db.Database.SqlQuery<QueueRankRow>(
            $@"WITH Ranked AS (
                SELECT wi.Id,
                    wi.LoanApplicationId,
                    wi.OwnerUserId,
                    wi.LeasedAt,
                    u.FirstName AS OwnerFirst,
                    u.LastName  AS OwnerLast,
                    ROW_NUMBER() OVER (
                        ORDER BY CASE WHEN wi.State = 'Active' THEN 0 ELSE 1 END,
                                 wi.EnqueuedAt, wi.Id
                    ) AS Position,
                    COUNT(*) OVER () AS PartitionSize
                FROM WorkflowQueueItems wi
                LEFT JOIN Users u ON u.Id = wi.OwnerUserId
                WHERE wi.PartitionKey = {partitionKey}
                  AND wi.State <> 'Completed'
            )
            SELECT LoanApplicationId, OwnerUserId, OwnerFirst, OwnerLast, LeasedAt,
                   CAST(Position AS INT) AS Position, CAST(PartitionSize AS INT) AS PartitionSize
            FROM Ranked WHERE LoanApplicationId = {loanId}")
            .ToListAsync(ct);
        var row = rows.FirstOrDefault();
        if (row is null) return null;
        var ownerName = row.OwnerFirst is null
            ? null
            : $"{row.OwnerFirst} {row.OwnerLast}";
        return new LoanQueueState(
            row.Position,
            row.Position == 1,
            row.OwnerUserId,
            ownerName,
            row.OwnerUserId == userId,
            row.LeasedAt);
    }
    public async Task<IReadOnlyDictionary<int, QueuePositionInfo>> GetPositionsAsync(
        IReadOnlyCollection<int> loanIds, CancellationToken ct)
    {
        if (loanIds.Count == 0) return new Dictionary<int, QueuePositionInfo>();
        var loanInfos = await _db.WorkflowQueueItems.AsNoTracking()
            .Where(i => loanIds.Contains(i.LoanApplicationId) && i.State != QueueItemState.Completed)
            .Select(i => new { i.LoanApplicationId, i.PartitionKey, i.Stage })
            .ToListAsync(ct);
        if (loanInfos.Count == 0) return new Dictionary<int, QueuePositionInfo>();
        var partitions = loanInfos.Select(i => i.PartitionKey).Distinct().ToList();
        var loanStageMap = loanInfos
            .GroupBy(i => i.LoanApplicationId)
            .ToDictionary(g => g.Key, g => g.First().Stage);
        var parameters = partitions
            .Select((k, i) => new SqlParameter($"@p{i}", k))
            .ToArray();
        var inClause = string.Join(", ",
            Enumerable.Range(0, partitions.Count).Select(i => $"@p{i}"));
        // State stored as string via HasConversion<string>() in WorkflowQueueItemConfiguration
        var sql = $@"SELECT wi.LoanApplicationId,
            wi.OwnerUserId,
            wi.LeasedAt,
            u.FirstName AS OwnerFirst,
            u.LastName  AS OwnerLast,
            CAST(ROW_NUMBER() OVER (
                PARTITION BY wi.PartitionKey
                ORDER BY CASE WHEN wi.State = 'Active' THEN 0 ELSE 1 END,
                         wi.EnqueuedAt, wi.Id
            ) AS INT) AS Position,
            CAST(COUNT(*) OVER (PARTITION BY wi.PartitionKey) AS INT) AS PartitionSize
           FROM WorkflowQueueItems wi
           LEFT JOIN Users u ON u.Id = wi.OwnerUserId
           WHERE wi.PartitionKey IN ({inClause})
             AND wi.State <> 'Completed'";
        var rows = await _db.Database.SqlQueryRaw<QueueRankRow>(sql, parameters)
            .ToListAsync(ct);
        var requestedSet = loanIds.ToHashSet();
        var result = new Dictionary<int, QueuePositionInfo>();
        foreach (var row in rows.Where(r => requestedSet.Contains(r.LoanApplicationId)))
        {
            var ownerName = row.OwnerFirst is null
                ? null
                : $"{row.OwnerFirst} {row.OwnerLast}";
            result[row.LoanApplicationId] = new QueuePositionInfo(
                loanStageMap[row.LoanApplicationId],
                row.Position,
                row.PartitionSize,
                row.OwnerUserId,
                ownerName,
                row.Position == 1);
        }
        return result;
    }
    public async Task<bool> IsHeadOwnerAsync(int loanId, int userId, string currentStatus, CancellationToken ct)
    {
        var stage = StageForStatus(currentStatus);
        if (stage == null) return true;
        var partitionKey = PartitionKey(stage.Value, new LoanApplication
        {
            BranchCode = "",
        });
        var queueItem = await _db.WorkflowQueueItems
            .AsNoTracking()
            .Where(i => i.LoanApplicationId == loanId
                        && i.Stage == stage.Value
                        && (i.State == QueueItemState.Active || i.State == QueueItemState.Queued))
            .Select(i => new { i.PartitionKey, i.OwnerUserId, i.State, i.EnqueuedAt, i.Id })
            .FirstOrDefaultAsync(ct);
        if (queueItem is null) return true;
        if (queueItem.State == QueueItemState.Active && queueItem.OwnerUserId == userId)
            return true;
        var headItem = await _db.WorkflowQueueItems
            .AsNoTracking()
            .Where(i => i.PartitionKey == queueItem.PartitionKey
                        && (i.State == QueueItemState.Active || i.State == QueueItemState.Queued))
            .OrderBy(i => i.State == QueueItemState.Active ? 0 : 1)
            .ThenBy(i => i.EnqueuedAt)
            .ThenBy(i => i.Id)
            .Select(i => new { i.LoanApplicationId, i.OwnerUserId, i.State })
            .FirstOrDefaultAsync(ct);
        return headItem is not null
               && headItem.LoanApplicationId == loanId
               && headItem.OwnerUserId == userId;
    }
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
    private async Task<(List<string> Keys, string Scope)> ApproverPartitionsAsync(int userId, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking()
            .Include(u => u.BranchCoverages)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user?.ApprovalAuthorityKey is null) return ([], "No authority assigned");
        var authorityKey = user.ApprovalAuthorityKey;
        var cacheKey = $"approval_authority:{authorityKey}";
        if (!_cache.TryGetValue(cacheKey, out ApprovalAuthority? authority))
        {
            authority = await _db.ApprovalAuthorities.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Key == authorityKey, ct);
            if (authority is not null)
                _cache.Set(cacheKey, authority, TimeSpan.FromMinutes(5));
        }
        if (authority is null) return ([], "No authority assigned");
        var branches = authority.ScopeType switch
        {
            AuthorityScope.Global => await GetCachedBranchCodesAsync(ct),
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
    private async Task<List<string>> GetCachedBranchCodesAsync(CancellationToken ct)
    {
        const string cacheKey = "branches:all";
        if (_cache.TryGetValue(cacheKey, out List<string>? codes) && codes is not null)
            return codes;
        codes = await _db.Branches.AsNoTracking()
            .Select(b => b.Code)
            .ToListAsync(ct);
        _cache.Set(cacheKey, codes, TimeSpan.FromMinutes(5));
        return codes;
    }
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
            var won = await TryLeaseItemAsync(candidate.Id, userId, ct);
            if (!won) continue;
            var loan = await _db.LoanApplications.AsNoTracking()
                .Where(l => l.Id == candidate.LoanApplicationId)
                .Select(l => new { l.Id, l.LamId, l.FirstName, l.LastName, l.Status })
                .FirstOrDefaultAsync(ct);
            if (loan is null) return null;
            var clientName = $"{loan.FirstName} {loan.LastName}".Trim();
            return new ClaimResponse(loan.Id, loan.LamId, clientName, loan.Status, now);
        }
        return null;
    }
    private async Task<bool> TryLeaseItemAsync(
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
        return won == 1;
    }
    public async Task<DeskQueueResponse> GetDeskAsync(
        int userId, string role, string branchCode, int page = 1, int pageSize = 20, CancellationToken ct = default)
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
            return new DeskQueueResponse(DeskLabelFor(role), [], null, scope, 0);

        var totalCount = await _db.WorkflowQueueItems.AsNoTracking()
            .Where(i => prefixes.Contains(i.PartitionKey) && i.State != QueueItemState.Completed)
            .CountAsync(ct);

        if (totalCount == 0)
            return new DeskQueueResponse(DeskLabelFor(role), [], null, scope, 0);

        var skip = (page - 1) * pageSize;
        var parameters = prefixes
            .Select((k, i) => new SqlParameter($"@p{i}", k))
            .ToArray();
        var inClause = string.Join(", ", Enumerable.Range(0, prefixes.Count).Select(i => $"@p{i}"));

        var sql = $@"
            WITH Ranked AS (
                SELECT wi.LoanApplicationId,
                       wi.OwnerUserId,
                       wi.EnqueuedAt,
                       u.FirstName + ' ' + u.LastName AS OwnerName,
                       la.LamId,
                       la.FirstName + ' ' + la.LastName AS ClientName,
                       la.Status,
                       la.BranchCode,
                       la.ProductCode,
                       la.Product,
                       la.CreationTypeLabel AS LoanType,
                       la.Purpose,
                       la.ProposedAmount,
                       la.TermDays,
                       la.ApplicationDate,
                       la.HasDeviations,
                       ROW_NUMBER() OVER (ORDER BY wi.EnqueuedAt, wi.Id) AS Rank
                FROM WorkflowQueueItems wi
                INNER JOIN LoanApplications la ON la.Id = wi.LoanApplicationId
                LEFT JOIN Users u ON u.Id = wi.OwnerUserId
                WHERE wi.PartitionKey IN ({inClause})
                  AND wi.State <> 'Completed'
            )
            SELECT * FROM Ranked
            ORDER BY Rank
            OFFSET {skip} ROWS FETCH NEXT {pageSize} ROWS ONLY";

        var rows = await _db.Database.SqlQueryRaw<DeskQueueRow>(sql, parameters).ToListAsync(ct);

        var dtos = rows.Select(row => new QueuedLoanDto(
            row.LoanApplicationId,
            row.LamId ?? "",
            (row.ClientName ?? "").Trim(),
            row.Rank,
            row.Rank == 1,
            row.OwnerUserId,
            row.OwnerName,
            row.EnqueuedAt,
            row.Status ?? "",
            row.BranchCode ?? "",
            row.ProductCode ?? "",
            row.Product ?? "",
            row.LoanType,
            row.Purpose,
            row.ProposedAmount,
            row.TermDays,
            row.ApplicationDate,
            row.HasDeviations)).ToList();

        var currentClaim = dtos.FirstOrDefault(d => d.OwnerUserId == userId);

        return new DeskQueueResponse(DeskLabelFor(role), dtos, currentClaim, scope, totalCount);
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
            .Select(i => new { i.Id, i.LoanApplicationId, i.OwnerUserId, i.State })
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
        var won = await TryLeaseItemAsync(item.Id, userId, ct);
        if (!won)
            return new ClaimByIdResult.LeasedByOther("another reviewer");
        var loan = await _db.LoanApplications.AsNoTracking()
            .Where(l => l.Id == item.LoanApplicationId)
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
    public async Task ExtendLeaseAsync(int loanId, int userId, CancellationToken ct)
    {
        var now = _time.UtcNow;
        await _db.WorkflowQueueItems
            .Where(i => i.LoanApplicationId == loanId
                        && i.State == QueueItemState.Active
                        && i.OwnerUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.LeasedAt, now), ct);
    }

    private class QueueRankRow
    {
        public int LoanApplicationId { get; set; }
        public int? OwnerUserId { get; set; }
        public string? OwnerFirst { get; set; }
        public string? OwnerLast { get; set; }
        public DateTime? LeasedAt { get; set; }
        public int Position { get; set; }
        public int PartitionSize { get; set; }
    }
    private class DeskQueueRow
    {
        public int LoanApplicationId { get; set; }
        public int? OwnerUserId { get; set; }
        public DateTime EnqueuedAt { get; set; }
        public string? OwnerName { get; set; }
        public string? LamId { get; set; }
        public string? ClientName { get; set; }
        public string? Status { get; set; }
        public string? BranchCode { get; set; }
        public string? ProductCode { get; set; }
        public string? Product { get; set; }
        public string? LoanType { get; set; }
        public string? Purpose { get; set; }
        public decimal ProposedAmount { get; set; }
        public int TermDays { get; set; }
        public DateTime ApplicationDate { get; set; }
        public bool HasDeviations { get; set; }
        public int Rank { get; set; }
    }
}

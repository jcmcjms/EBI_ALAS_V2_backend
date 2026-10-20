using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Workflow;

/// <summary>
/// Branch desk queue grouped per client. Applications submitted as one bundle
/// share an ApplicationGroupNo; the desk serves, claims and releases the whole
/// bundle as a single unit so one reviewer owns every application of a client.
/// </summary>
public sealed class DeskQueueService(
    AlasDbContext db,
    WorkflowQueueService queue,
    TimeProvider timeProvider)
{
    public const int PageSize = 50;

    public async Task<DeskQueueResult> GetMyDeskAsync(CallerContext caller, CancellationToken cancellationToken)
    {
        if (!TryMapStage(caller.Role, out var stage))
        {
            return new DeskQueueResult(DeskLabelFor(caller.Role), [], null, "No review desk for this role.");
        }

        var partition = caller.BranchId;
        await queue.ReapExpiredAsync(stage, partition, cancellationToken);
        await ReleaseCancelledClaimsAsync(stage, partition, cancellationToken);

        var rows = await LoadRowsAsync(stage, partition, tracked: false, cancellationToken);
        var names = await LoadOwnerNamesAsync(rows, cancellationToken);
        var clients = BuildClients(rows, names);

        var current = clients.FirstOrDefault(c => c.OwnerUserId == caller.UserId);
        var queued = clients.Where(c => c.OwnerUserId is null).ToList();

        return new DeskQueueResult(
            DeskLabelFor(caller.Role),
            queued,
            current,
            $"Branch {partition} · {queued.Count} client{(queued.Count == 1 ? string.Empty : "s")} waiting");
    }

    public async Task<DeskClaimResult?> ClaimHeadAsync(CallerContext caller, CancellationToken cancellationToken)
    {
        if (!TryMapStage(caller.Role, out var stage))
        {
            return null;
        }

        var partition = caller.BranchId;
        await ReleaseCancelledClaimsAsync(stage, partition, cancellationToken);

        var owned = await db.WorkflowQueueItems
            .Where(q => q.OwnerUserId == caller.UserId && q.Stage == stage && q.State == QueueItemState.Active)
            .OrderBy(q => q.Sequence)
            .ToListAsync(cancellationToken);
        if (owned.Count > 0)
        {
            return await ResumeOwnedBundleAsync(stage, partition, caller.UserId, owned, cancellationToken);
        }

        await queue.ReapExpiredAsync(stage, partition, cancellationToken);

        var rows = await LoadRowsAsync(stage, partition, tracked: true, cancellationToken);
        var head = GroupRows(rows).FirstOrDefault(g => g.Items.All(i => i.State == QueueItemState.Queued));
        if (head is null)
        {
            return null;
        }

        return await ClaimGroupAsync(stage, partition, caller.UserId, head.GroupNo, head, cancellationToken);
    }

    public async Task<bool> ReleaseClaimAsync(CallerContext caller, CancellationToken cancellationToken)
    {
        var actives = await db.WorkflowQueueItems
            .Where(q => q.OwnerUserId == caller.UserId && q.State == QueueItemState.Active)
            .OrderBy(q => q.Sequence)
            .ToListAsync(cancellationToken);
        if (actives.Count == 0)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var item in actives)
        {
            item.Release(caller.UserId, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<DeskClaimResult?> ClaimByIdAsync(Guid loanId, CallerContext caller, CancellationToken cancellationToken)
    {
        if (!TryMapStage(caller.Role, out var stage))
        {
            return null;
        }

        var requested = await db.WorkflowQueueItems
            .FirstOrDefaultAsync(q => q.LoanApplicationId == loanId && q.Stage == stage, cancellationToken);
        if (requested is null)
        {
            return null;
        }

        var partition = requested.PartitionKey;

        if (requested.State == QueueItemState.Active && requested.OwnerUserId != caller.UserId)
        {
            throw new ConflictException("Currently with another officer.");
        }

        if (requested.State == QueueItemState.Active && requested.OwnerUserId == caller.UserId)
        {
            var owned = await db.WorkflowQueueItems
                .Where(q => q.OwnerUserId == caller.UserId && q.Stage == stage && q.State == QueueItemState.Active)
                .OrderBy(q => q.Sequence)
                .ToListAsync(cancellationToken);
            return await ResumeOwnedBundleAsync(stage, partition, caller.UserId, owned, cancellationToken);
        }

        var rows = await LoadRowsAsync(stage, partition, tracked: true, cancellationToken);
        var groups = GroupRows(rows);
        var target = groups.FirstOrDefault(g => g.Items.Any(i => i.LoanApplicationId == loanId));
        if (target is null)
        {
            return null;
        }

        if (target.Items.Any(i => i.State == QueueItemState.Active))
        {
            throw new ConflictException("Currently with another officer.");
        }

        var head = groups.FirstOrDefault(g => g.Items.All(i => i.State == QueueItemState.Queued));
        if (head is null || head.GroupNo != target.GroupNo)
        {
            throw new ConflictException("This client is queued behind another application.");
        }

        return await ClaimGroupAsync(stage, partition, caller.UserId, target.GroupNo, target, cancellationToken);
    }

    private async Task<DeskClaimResult?> ClaimGroupAsync(
        WorkflowStage stage,
        string partition,
        Guid userId,
        string groupNo,
        DeskGroup loaded,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // The loaded page may cut a bundle at the PageSize boundary; re-query so
        // every queued application of the client is claimed together.
        var claimedIds = loaded.Items.Select(i => i.LoanApplicationId).ToHashSet();
        var extra = await (
            from q in db.WorkflowQueueItems
            where q.Stage == stage
                && q.PartitionKey == partition
                && q.State == QueueItemState.Queued
                && !claimedIds.Contains(q.LoanApplicationId)
            join l in db.LoanApplications on q.LoanApplicationId equals l.Id
            where l.ApplicationGroupNo == groupNo && l.Status != LoanStatus.Cancelled
            orderby q.Sequence
            select new { q, l }
        ).ToListAsync(cancellationToken);

        var items = loaded.Items.ToList();
        var loans = loaded.Loans.ToList();
        foreach (var row in extra)
        {
            items.Add(row.q);
            loans.Add(row.l);
        }

        foreach (var item in items)
        {
            if (!item.TryClaim(userId, now))
            {
                throw new ConflictException("Queue item was claimed concurrently.");
            }
        }

        foreach (var loan in loans)
        {
            loan.AssignTo(userId, now);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Queue item was claimed concurrently.");
        }

        return BuildClaimResult(items, loans, now);
    }

    private async Task<DeskClaimResult?> ResumeOwnedBundleAsync(
        WorkflowStage stage,
        string partition,
        Guid userId,
        List<WorkflowQueueItem> owned,
        CancellationToken cancellationToken)
    {
        var loanIds = owned.Select(i => i.LoanApplicationId).ToList();
        var loansById = await db.LoanApplications
            .Where(l => loanIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, cancellationToken);

        var items = new List<WorkflowQueueItem>();
        var loans = new List<LoanApplication>();
        foreach (var item in owned)
        {
            if (!loansById.TryGetValue(item.LoanApplicationId, out var loan)
                || loan.Status == LoanStatus.Cancelled)
            {
                continue;
            }

            items.Add(item);
            loans.Add(loan);
        }

        if (loans.Count == 0)
        {
            return null;
        }

        var groupNo = loans[0].ApplicationGroupNo;
        var now = timeProvider.GetUtcNow();

        // A sibling may have re-entered the queue (e.g. after revision) while the
        // bundle was held; absorb it so the client stays with one reviewer.
        var heldIds = loans.Select(l => l.Id).ToHashSet();
        var queuedSiblings = await (
            from q in db.WorkflowQueueItems
            where q.Stage == stage
                && q.PartitionKey == partition
                && q.State == QueueItemState.Queued
                && !heldIds.Contains(q.LoanApplicationId)
            join l in db.LoanApplications on q.LoanApplicationId equals l.Id
            where l.ApplicationGroupNo == groupNo && l.Status != LoanStatus.Cancelled
            orderby q.Sequence
            select new { q, l }
        ).ToListAsync(cancellationToken);

        foreach (var sibling in queuedSiblings)
        {
            if (!sibling.q.TryClaim(userId, now))
            {
                continue;
            }

            sibling.l.AssignTo(userId, now);
            items.Add(sibling.q);
            loans.Add(sibling.l);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Queue item was claimed concurrently.");
        }

        return BuildClaimResult(items, loans, items[0].LeasedAt ?? now);
    }

    private static DeskClaimResult BuildClaimResult(
        IReadOnlyList<WorkflowQueueItem> items,
        IReadOnlyList<LoanApplication> loans,
        DateTimeOffset leasedAt)
    {
        var primary = loans[0];
        var applications = loans
            .Select(l => new DeskClaimApplicationDto(l.Id, l.LamId, l.Status.ToString()))
            .ToList();
        return new DeskClaimResult(
            primary.Id,
            primary.LamId,
            primary.ClientName,
            primary.Status.ToString(),
            leasedAt,
            primary.ApplicationGroupNo,
            applications);
    }

    private async Task ReleaseCancelledClaimsAsync(
        WorkflowStage stage,
        string partition,
        CancellationToken cancellationToken)
    {
        var items = await (
            from q in db.WorkflowQueueItems
            where q.Stage == stage
                && q.PartitionKey == partition
                && (q.State == QueueItemState.Queued || q.State == QueueItemState.Active)
            join l in db.LoanApplications on q.LoanApplicationId equals l.Id
            where l.Status == LoanStatus.Cancelled
            select q
        ).ToListAsync(cancellationToken);
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

    private async Task<List<(WorkflowQueueItem QueueItem, LoanApplication Loan)>> LoadRowsAsync(
        WorkflowStage stage,
        string partition,
        bool tracked,
        CancellationToken cancellationToken)
    {
        var queueItems = tracked ? db.WorkflowQueueItems : db.WorkflowQueueItems.AsNoTracking();
        var loans = tracked ? db.LoanApplications : db.LoanApplications.AsNoTracking();

        var rows = await (
            from q in queueItems
            where q.Stage == stage
                && q.PartitionKey == partition
                && (q.State == QueueItemState.Queued || q.State == QueueItemState.Active)
            join l in loans on q.LoanApplicationId equals l.Id
            orderby q.Sequence
            select new { q, l }
        ).Take(PageSize).ToListAsync(cancellationToken);

        return rows.Select(r => (r.q, r.l)).ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadOwnerNamesAsync(
        IReadOnlyList<(WorkflowQueueItem QueueItem, LoanApplication Loan)> rows,
        CancellationToken cancellationToken)
    {
        var owners = rows
            .Where(r => r.QueueItem.OwnerUserId is not null)
            .Select(r => r.QueueItem.OwnerUserId!.Value)
            .Distinct()
            .ToList();
        if (owners.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await db.Users.AsNoTracking()
            .Where(u => owners.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
    }

    private static List<DeskQueueClientDto> BuildClients(
        IReadOnlyList<(WorkflowQueueItem QueueItem, LoanApplication Loan)> rows,
        IReadOnlyDictionary<Guid, string> names)
    {
        var flatPositions = rows
            .Select((r, index) => (r.QueueItem.Id, Position: index + 1))
            .ToDictionary(x => x.Id, x => x.Position);

        var clients = new List<DeskQueueClientDto>();
        var position = 0;
        foreach (var group in GroupRows(rows))
        {
            position++;

            Guid? ownerUserId = null;
            foreach (var item in group.Items)
            {
                if (item.State == QueueItemState.Active && item.OwnerUserId is not null)
                {
                    ownerUserId = item.OwnerUserId;
                    break;
                }
            }

            string? ownerName = null;
            if (ownerUserId is { } ownerId)
            {
                names.TryGetValue(ownerId, out ownerName);
            }

            var applications = new List<DeskQueueItemDto>(group.Items.Count);
            for (var i = 0; i < group.Items.Count; i++)
            {
                applications.Add(ToItemDto(group.Items[i], group.Loans[i], flatPositions[group.Items[i].Id], names));
            }

            clients.Add(new DeskQueueClientDto(
                group.GroupNo,
                group.Loans[0].ClientName,
                position,
                position == 1,
                ownerUserId,
                ownerName,
                group.Items.Min(item => item.QueuedAt),
                applications));
        }

        return clients;
    }

    private sealed record DeskGroup(
        string GroupNo,
        IReadOnlyList<WorkflowQueueItem> Items,
        IReadOnlyList<LoanApplication> Loans);

    private static List<DeskGroup> GroupRows(
        IEnumerable<(WorkflowQueueItem QueueItem, LoanApplication Loan)> rows)
    {
        return rows
            .GroupBy(r => r.Loan.ApplicationGroupNo)
            .Select(g =>
            {
                var ordered = g.OrderBy(r => r.QueueItem.Sequence).ToList();
                return new DeskGroup(
                    g.Key,
                    ordered.Select(r => r.QueueItem).ToList(),
                    ordered.Select(r => r.Loan).ToList());
            })
            .OrderBy(g => g.Items[0].Sequence)
            .ToList();
    }

    private static DeskQueueItemDto ToItemDto(
        WorkflowQueueItem q,
        LoanApplication l,
        int position,
        IReadOnlyDictionary<Guid, string> names)
    {
        string? ownerName = null;
        if (q.OwnerUserId is { } ownerId)
        {
            names.TryGetValue(ownerId, out ownerName);
        }

        return new DeskQueueItemDto(
            l.Id,
            l.LamId,
            l.ClientName,
            position,
            position == 1,
            q.OwnerUserId,
            ownerName,
            q.QueuedAt,
            l.Status.ToString(),
            l.BranchId,
            string.Empty,
            string.Empty,
            l.LoanType.ToString(),
            null,
            l.Principal,
            l.TermDays,
            l.CreatedAt,
            false);
    }

    public static bool TryMapStage(UserRole role, out WorkflowStage stage)
    {
        switch (role)
        {
            case UserRole.Recommender:
                stage = WorkflowStage.Recommendation;
                return true;
            case UserRole.Evaluator:
                stage = WorkflowStage.Evaluation;
                return true;
            case UserRole.Approver:
                stage = WorkflowStage.Approval;
                return true;
            default:
                stage = default;
                return false;
        }
    }

    private static string DeskLabelFor(UserRole role) => role switch
    {
        UserRole.Recommender => "Recommendation Desk",
        UserRole.Evaluator => "Evaluation Desk",
        UserRole.Approver => "Approval Desk",
        _ => "Review Desk",
    };
}

using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Workflow;

/// <summary>Role-scoped review desk over the workflow queue.</summary>
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

        var items = await LoadItemsAsync(stage, partition, cancellationToken);
        var current = items.FirstOrDefault(i => i.OwnerUserId == caller.UserId);
        var queued = items.Where(i => i.OwnerUserId is null).ToList();

        return new DeskQueueResult(
            DeskLabelFor(caller.Role),
            queued,
            current,
            $"Branch {partition} · {queued.Count} waiting");
    }

    public async Task<DeskClaimResult?> ClaimHeadAsync(CallerContext caller, CancellationToken cancellationToken)
    {
        if (!TryMapStage(caller.Role, out var stage))
        {
            return null;
        }

        var existing = await db.WorkflowQueueItems
            .FirstOrDefaultAsync(q => q.OwnerUserId == caller.UserId && q.State == QueueItemState.Active, cancellationToken);
        if (existing is not null)
        {
            var loan = await db.LoanApplications.AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == existing.LoanApplicationId, cancellationToken);
            return loan is null
                ? null
                : new DeskClaimResult(loan.Id, loan.LamId, loan.ClientName, loan.Status.ToString(), existing.LeasedAt ?? timeProvider.GetUtcNow());
        }

        try
        {
            var item = await queue.ClaimNextAsync(stage, caller.BranchId, caller.UserId, cancellationToken);
            var claimed = await db.LoanApplications.AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == item.LoanApplicationId, cancellationToken);
            return claimed is null
                ? null
                : new DeskClaimResult(claimed.Id, claimed.LamId, claimed.ClientName, claimed.Status.ToString(), item.LeasedAt ?? timeProvider.GetUtcNow());
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    public async Task<bool> ReleaseClaimAsync(CallerContext caller, CancellationToken cancellationToken)
    {
        var active = await db.WorkflowQueueItems
            .FirstOrDefaultAsync(q => q.OwnerUserId == caller.UserId && q.State == QueueItemState.Active, cancellationToken);
        if (active is null)
        {
            return false;
        }

        await queue.ReleaseAsync(active.Id, caller.UserId, cancellationToken);
        return true;
    }

    public async Task<DeskClaimResult?> ClaimByIdAsync(Guid loanId, CallerContext caller, CancellationToken cancellationToken)
    {
        if (!TryMapStage(caller.Role, out var stage))
        {
            return null;
        }

        var item = await db.WorkflowQueueItems
            .FirstOrDefaultAsync(q => q.LoanApplicationId == loanId && q.Stage == stage, cancellationToken);
        if (item is null)
        {
            return null;
        }

        if (item.State == QueueItemState.Active && item.OwnerUserId != caller.UserId)
        {
            throw new ConflictException("Currently with another officer.");
        }

        if (item.State == QueueItemState.Queued)
        {
            var head = await db.WorkflowQueueItems
                .Where(q => q.Stage == stage && q.PartitionKey == item.PartitionKey && q.State == QueueItemState.Queued)
                .OrderBy(q => q.Sequence)
                .FirstOrDefaultAsync(cancellationToken);
            if (head is null || head.Id != item.Id)
            {
                throw new ConflictException("This file is queued behind another application.");
            }

            if (!item.TryClaim(caller.UserId, timeProvider.GetUtcNow()))
            {
                throw new ConflictException("Queue item is already claimed.");
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        var loan = await db.LoanApplications.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);
        return loan is null
            ? null
            : new DeskClaimResult(loan.Id, loan.LamId, loan.ClientName, loan.Status.ToString(), item.LeasedAt ?? timeProvider.GetUtcNow());
    }

    private async Task<IReadOnlyList<DeskQueueItemDto>> LoadItemsAsync(
        WorkflowStage stage,
        string partition,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from q in db.WorkflowQueueItems.AsNoTracking()
            where q.Stage == stage && q.PartitionKey == partition
            join l in db.LoanApplications.AsNoTracking() on q.LoanApplicationId equals l.Id
            orderby q.Sequence
            select new { q, l }
        ).Take(PageSize).ToListAsync(cancellationToken);

        var owners = rows.Where(r => r.q.OwnerUserId is not null)
            .Select(r => r.q.OwnerUserId!.Value)
            .Distinct()
            .ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => owners.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        return rows.Select((r, index) => ToDto(r.q, r.l, index + 1, names)).ToList();
    }

    private static DeskQueueItemDto ToDto(
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

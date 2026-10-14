using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Dashboard;

public sealed record FlaggedDocumentItem(
    Guid Id,
    string LamId,
    string BranchId,
    DateTimeOffset WaitingSinceUtc,
    int MissingCount,
    string ClientName,
    string EncoderName,
    string? FlaggedByName,
    DateTimeOffset? FlaggedAt);

public sealed record PendingQueueItem(
    int Position,
    string LamId,
    string BranchId,
    string Status,
    string StatusKey,
    DateTimeOffset Date,
    string ClientName,
    string EncoderName);

public sealed record NowServingItem(
    int Number,
    string Checker,
    string LamId,
    bool IsActive);

public sealed record PushBackItem(
    int Number,
    string LamId,
    string BranchId,
    DateTimeOffset Date,
    string Reason);

public sealed record ApprovedLoanItem(
    string BranchId,
    string LamId,
    DateTimeOffset Date,
    string FullName);

public sealed record WeeklyTrendPoint(
    string Day,
    int Approved,
    int PushBacks);

public sealed record DashboardOverview(
    int PendingRecommendation,
    int PendingEvaluation,
    int PendingApproval,
    int ApprovedToday,
    int Pushbacks,
    int NowServingCount,
    IReadOnlyList<FlaggedDocumentItem> FlaggedDocuments,
    IReadOnlyList<PendingQueueItem> PendingQueue,
    IReadOnlyList<NowServingItem> NowServing,
    IReadOnlyList<PushBackItem> PushBacks,
    IReadOnlyList<ApprovedLoanItem> ApprovedLoans,
    IReadOnlyList<WeeklyTrendPoint> WeeklyTrend);

public sealed class DashboardService(AlasDbContext db, TimeProvider timeProvider)
{
    public const int ListCap = 50;
    public const int FlaggedDocumentsCap = 20;

    public async Task<DashboardOverview> GetOverviewAsync(CallerContext caller, CancellationToken cancellationToken)
    {
        var today = timeProvider.GetUtcNow().Date;
        var query = db.LoanApplications.AsQueryable();
        if (!caller.CanAccessAllBranches)
        {
            query = query.Where(l => l.BranchId == caller.BranchId);
        }

        var counts = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                PendingRecommendation = g.Count(l => l.Status == LoanStatus.ForRecommendation),
                PendingEvaluation = g.Count(l => l.Status == LoanStatus.ForChecking),
                PendingApproval = g.Count(l => l.Status == LoanStatus.ForApproval),
                ApprovedToday = g.Count(l => l.Status == LoanStatus.Approved && l.UpdatedAt >= today),
                Pushbacks = g.Count(l => l.Status == LoanStatus.ForRevision),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var scoped = db.LoanApplications.AsNoTracking();
        if (!caller.CanAccessAllBranches)
        {
            scoped = scoped.Where(l => l.BranchId == caller.BranchId);
        }

        var pendingRows = await scoped
            .Where(l => l.Status == LoanStatus.ForRecommendation
                || l.Status == LoanStatus.ForChecking
                || l.Status == LoanStatus.ForApproval)
            .OrderBy(l => l.LastActionDate)
            .Take(ListCap)
            .Select(l => new
            {
                l.Id,
                l.LamId,
                l.BranchId,
                Status = l.Status.ToString(),
                l.LastActionDate,
                l.ClientName,
                l.CreatedBy,
            })
            .ToListAsync(cancellationToken);

        var servingRows = await (
            from q in db.WorkflowQueueItems.AsNoTracking()
            where q.State == QueueItemState.Active && q.OwnerUserId != null
            join l in db.LoanApplications.AsNoTracking() on q.LoanApplicationId equals l.Id
            where caller.CanAccessAllBranches || l.BranchId == caller.BranchId
            orderby q.LeasedAt
            select new
            {
                q.Id,
                q.OwnerUserId,
                l.LamId,
                q.LeasedAt,
            })
        .Take(ListCap)
        .ToListAsync(cancellationToken);

        var pushbackRows = await scoped
            .Where(l => l.Status == LoanStatus.ForRevision)
            .OrderByDescending(l => l.LastActionDate)
            .Take(ListCap)
            .Select(l => new
            {
                l.LamId,
                l.BranchId,
                l.LastActionDate,
                l.ClientName,
                l.CreatedBy,
            })
            .ToListAsync(cancellationToken);

        var approvedRows = await scoped
            .Where(l => l.Status == LoanStatus.Approved)
            .OrderByDescending(l => l.LastActionDate)
            .Take(ListCap)
            .Select(l => new
            {
                l.LamId,
                l.BranchId,
                l.LastActionDate,
                l.ClientName,
            })
            .ToListAsync(cancellationToken);

        var weekStart = today.AddDays(-6);
        var actionQuery = db.LoanApplicationActions.AsNoTracking();
        var weekActions = await (
            from a in actionQuery
            join l in db.LoanApplications.AsNoTracking() on a.LoanApplicationId equals l.Id
            where a.ActionDate >= weekStart
                && (caller.CanAccessAllBranches || l.BranchId == caller.BranchId)
            select new { a.Action, a.ActionDate }
        ).ToListAsync(cancellationToken);

        var flagged = await LoadFlaggedAsync(caller, cancellationToken);

        var personIds = pendingRows.Select(r => r.CreatedBy)
            .Concat(servingRows.Select(r => r.OwnerUserId!.Value))
            .Concat(pushbackRows.Select(r => r.CreatedBy))
            .Distinct()
            .ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => personIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim(), cancellationToken);

        var pendingQueue = pendingRows
            .Select((r, i) =>
            {
                names.TryGetValue(r.CreatedBy, out var encoderName);
                return new PendingQueueItem(
                    i + 1,
                    r.LamId,
                    r.BranchId,
                    r.Status,
                    r.Status.Replace(" ", string.Empty),
                    r.LastActionDate,
                    r.ClientName,
                    encoderName ?? string.Empty);
            })
            .ToList();

        var nowServing = servingRows
            .Select((r, i) =>
            {
                names.TryGetValue(r.OwnerUserId!.Value, out var checker);
                return new NowServingItem(i + 1, checker ?? "Reviewer", r.LamId, true);
            })
            .ToList();

        var pushBacks = pushbackRows
            .Select((r, i) =>
            {
                names.TryGetValue(r.CreatedBy, out var encoderName);
                return new PushBackItem(
                    i + 1,
                    r.LamId,
                    r.BranchId,
                    r.LastActionDate,
                    $"Returned to {encoderName ?? "encoder"}");
            })
            .ToList();

        var approvedLoans = approvedRows
            .Select(r => new ApprovedLoanItem(r.BranchId, r.LamId, r.LastActionDate, r.ClientName))
            .ToList();

        var trend = Enumerable.Range(0, 7)
            .Select(offset =>
            {
                var day = weekStart.AddDays(offset);
                var dayActions = weekActions.Where(a => a.ActionDate.Date == day).ToList();
                return new WeeklyTrendPoint(
                    day.ToString("ddd"),
                    dayActions.Count(a => a.Action == "Approved"),
                    dayActions.Count(a => a.Action == "PushedBack"));
            })
            .ToList();

        return new DashboardOverview(
            counts?.PendingRecommendation ?? 0,
            counts?.PendingEvaluation ?? 0,
            counts?.PendingApproval ?? 0,
            counts?.ApprovedToday ?? 0,
            counts?.Pushbacks ?? 0,
            nowServing.Count,
            flagged,
            pendingQueue,
            nowServing,
            pushBacks,
            approvedLoans,
            trend);
    }

    private async Task<IReadOnlyList<FlaggedDocumentItem>> LoadFlaggedAsync(
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        var flaggedQuery = db.LoanApplications.AsNoTracking();
        if (!caller.CanAccessAllBranches)
        {
            flaggedQuery = flaggedQuery.Where(l => l.BranchId == caller.BranchId);
        }

        var flaggedRows = await flaggedQuery
            .Where(l => l.DocumentsFlaggedAt != null)
            .OrderBy(l => l.DocumentsFlaggedAt)
            .Take(FlaggedDocumentsCap)
            .Select(l => new
            {
                l.Id,
                l.LamId,
                l.BranchId,
                l.ClientName,
                l.DocumentFlagMissingCount,
                l.DocumentsFlaggedAt,
                l.DocumentsFlaggedById,
                l.CreatedBy,
                l.LastActionDate,
            })
            .ToListAsync(cancellationToken);

        var userIds = flaggedRows
            .SelectMany(r => new[] { r.CreatedBy, r.DocumentsFlaggedById ?? r.CreatedBy })
            .Distinct()
            .ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim(), cancellationToken);

        return flaggedRows
            .Select(r =>
            {
                names.TryGetValue(r.CreatedBy, out var encoderName);
                string? flaggedByName = null;
                if (r.DocumentsFlaggedById is { } flaggerId && names.TryGetValue(flaggerId, out var name))
                {
                    flaggedByName = name;
                }

                return new FlaggedDocumentItem(
                    r.Id,
                    r.LamId,
                    r.BranchId,
                    r.DocumentsFlaggedAt ?? r.LastActionDate,
                    r.DocumentFlagMissingCount,
                    r.ClientName,
                    encoderName ?? string.Empty,
                    flaggedByName,
                    r.DocumentsFlaggedAt);
            })
            .ToList();
    }
}

using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Pagination;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
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
    IReadOnlyList<PushBackItem> PushBackItems,
    IReadOnlyList<ApprovedLoanItem> ApprovedLoans,
    IReadOnlyList<WeeklyTrendPoint> WeeklyTrend,
    bool RequireRecommendation);

public sealed class DashboardService(AlasDbContext db, IWorkflowConfiguration workflow)
{
    public const int ListCap = 50;
    public const int FlaggedDocumentsCap = 20;

    public static readonly TimeZoneInfo Manila =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

    public static (DateTimeOffset Start, DateTimeOffset EndExclusive) ManilaDayRange(DateOnly day)
    {
        var startLocal = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = Manila.GetUtcOffset(startLocal);
        var start = new DateTimeOffset(startLocal, offset);
        return (start, start.AddDays(1));
    }

    public static DateOnly ManilaToday() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Manila).Date);

    public async Task<DashboardOverview> GetOverviewAsync(
        CallerContext caller,
        DateOnly asOfDay,
        CancellationToken cancellationToken)
    {
        var (dayStart, dayEnd) = ManilaDayRange(asOfDay);
        await workflow.EnsureLoadedAsync(cancellationToken);
        var readable = await ResolveReadableBranchesAsync(caller, cancellationToken);
        var query = ApplyBranchScope(db.LoanApplications.AsQueryable(), readable);

        var counts = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                PendingRecommendation = g.Count(l => l.Status == LoanStatus.ForRecommendation),
                PendingEvaluation = g.Count(l => l.Status == LoanStatus.ForChecking),
                PendingApproval = g.Count(l => l.Status == LoanStatus.ForApproval),
                ApprovedToday = g.Count(l => l.Status == LoanStatus.Approved
                    && l.LastActionDate >= dayStart
                    && l.LastActionDate < dayEnd),
                Pushbacks = g.Count(l => l.Status == LoanStatus.ForRevision
                    && l.LastActionDate >= dayStart
                    && l.LastActionDate < dayEnd),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var scoped = ApplyBranchScope(db.LoanApplications.AsNoTracking(), readable);

        var isEncoderScope = caller.Role == UserRole.Encoder;

        var pendingQuery = scoped
            .Where(l => l.Status == LoanStatus.ForRecommendation
                || l.Status == LoanStatus.ForChecking
                || l.Status == LoanStatus.ForApproval);
        if (isEncoderScope)
        {
            pendingQuery = pendingQuery.Where(l => l.CreatedBy == caller.UserId);
        }

        var pendingRows = await pendingQuery
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
            where (readable == null || readable.Contains(l.BranchId))
                && (!isEncoderScope || q.OwnerUserId == caller.UserId)
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

        var pushbackQuery = scoped.Where(l => l.Status == LoanStatus.ForRevision
            && l.LastActionDate >= dayStart
            && l.LastActionDate < dayEnd);
        if (isEncoderScope)
        {
            pushbackQuery = pushbackQuery.Where(l => l.CreatedBy == caller.UserId);
        }

        var pushbackRows = await pushbackQuery
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

        var approvedQuery = scoped.Where(l => l.Status == LoanStatus.Approved
            && l.LastActionDate >= dayStart
            && l.LastActionDate < dayEnd);
        if (isEncoderScope)
        {
            approvedQuery = approvedQuery.Where(l => l.CreatedBy == caller.UserId);
        }

        var approvedRows = await approvedQuery
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

        var weekStartDate = asOfDay.AddDays(-6);
        var (weekStart, _) = ManilaDayRange(weekStartDate);
        var (_, weekEnd) = ManilaDayRange(asOfDay);
        var actionQuery = db.LoanApplicationActions.AsNoTracking();
        var weekActions = await (
            from a in actionQuery
            join l in db.LoanApplications.AsNoTracking() on a.LoanApplicationId equals l.Id
            where a.ActionDate >= weekStart
                && a.ActionDate < weekEnd
                && (readable == null || readable.Contains(l.BranchId))
            select new { a.Action, a.ActionDate }
        ).ToListAsync(cancellationToken);

        var flagged = await LoadFlaggedAsync(caller, readable, cancellationToken);

        var personIds = pendingRows.Select(r => r.CreatedBy)
            .Concat(servingRows.Select(r => r.OwnerUserId!.Value))
            .Concat(pushbackRows.Select(r => r.CreatedBy))
            .Distinct()
            .ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => personIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim(), cancellationToken);

        var pendingQueue = pendingRows
            .GroupBy(r => r.Status)
            .SelectMany(group => group.Select((r, i) =>
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
            }))
            .ToList();

        var nowServing = servingRows
            .Select((r, i) =>
            {
                names.TryGetValue(r.OwnerUserId!.Value, out var checker);
                return new NowServingItem(i + 1, checker ?? "Reviewer", r.LamId, true);
            })
            .ToList();

        var pushBackItems = pushbackRows
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
                var day = weekStartDate.AddDays(offset);
                var (dStart, dEnd) = ManilaDayRange(day);
                var dayActions = weekActions
                    .Where(a => a.ActionDate >= dStart && a.ActionDate < dEnd)
                    .ToList();
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
            pushBackItems,
            approvedLoans,
            trend,
            workflow.RequireRecommendation);
    }

    public async Task<PageResult<PendingQueueItem>> GetPendingQueueAsync(
        CallerContext caller,
        LoanStatus status,
        PageRequest page,
        int maxPageSize,
        int defaultPageSize,
        CancellationToken cancellationToken)
    {
        var normalized = page.Normalize(maxPageSize, defaultPageSize);
        var readable = await ResolveReadableBranchesAsync(caller, cancellationToken);
        var query = ApplyBranchScope(db.LoanApplications.AsNoTracking(), readable)
            .Where(l => l.Status == status);

        if (caller.Role == UserRole.Encoder)
        {
            query = query.Where(l => l.CreatedBy == caller.UserId);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(l => l.LastActionDate)
            .ThenBy(l => l.LamId)
            .Skip(normalized.Skip)
            .Take(normalized.PageSize)
            .Select(l => new
            {
                l.LamId,
                l.BranchId,
                Status = l.Status.ToString(),
                l.LastActionDate,
                l.ClientName,
                l.CreatedBy,
            })
            .ToListAsync(cancellationToken);

        var personIds = rows.Select(r => r.CreatedBy).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => personIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim(), cancellationToken);

        var items = rows.Select((r, i) =>
        {
            names.TryGetValue(r.CreatedBy, out var encoderName);
            return new PendingQueueItem(
                normalized.Skip + i + 1,
                r.LamId,
                r.BranchId,
                r.Status,
                r.Status.Replace(" ", string.Empty),
                r.LastActionDate,
                r.ClientName,
                encoderName ?? string.Empty);
        }).ToList();

        return new PageResult<PendingQueueItem>(items, total, normalized.Page, normalized.PageSize);
    }

    private static IQueryable<LoanApplication> ApplyBranchScope(
        IQueryable<LoanApplication> query,
        IReadOnlyList<string>? readableBranches)
    {
        if (readableBranches is null)
        {
            return query;
        }

        return query.Where(l => readableBranches.Contains(l.BranchId));
    }

    /// <summary>null means org-wide (no branch filter).</summary>
    private async Task<IReadOnlyList<string>?> ResolveReadableBranchesAsync(
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        if (caller.CanViewAllLoanBranches)
        {
            return null;
        }

        var jobTitle = await db.Users.AsNoTracking()
            .Where(u => u.Id == caller.UserId)
            .Select(u => u.JobTitle)
            .FirstOrDefaultAsync(cancellationToken);

        if (Branches.BranchVisibility.IsGlobalAuthority(jobTitle))
        {
            return null;
        }

        return await Branches.BranchVisibility.GetReadableBranchIdsAsync(
            db,
            caller.UserId,
            caller.Role,
            caller.BranchId,
            cancellationToken,
            jobTitle);
    }

    private async Task<IReadOnlyList<FlaggedDocumentItem>> LoadFlaggedAsync(
        CallerContext caller,
        IReadOnlyList<string>? readableBranches,
        CancellationToken cancellationToken)
    {
        var flaggedQuery = ApplyBranchScope(db.LoanApplications.AsNoTracking(), readableBranches);

        if (caller.Role == UserRole.Encoder)
        {
            flaggedQuery = flaggedQuery.Where(l => l.CreatedBy == caller.UserId);
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

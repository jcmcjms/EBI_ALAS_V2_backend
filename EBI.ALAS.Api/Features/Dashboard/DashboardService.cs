using System.Globalization;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;


namespace EBI.ALAS.Api.Features.Dashboard;

public class DashboardService : IDashboardService
{
    // Statuses where a human action is still owed — this IS the "pending" queue.
    private static readonly string[] PendingStatuses =
        ["ForRecommendation", "ForChecking", "ForApproval", "ForRevision"];

    private const int QueueSize = 6;
    private const int ListSize = 5;
    private const int ActiveProbeCap = 200;
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan ServingWindow = TimeSpan.FromMinutes(15);

    // PH has no DST, so UTC+8 is exact — day buckets match branch wall-clock.
    private static readonly TimeSpan PhOffset = TimeSpan.FromHours(8);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(15);
    private const string AllBranchesKey = "ALL";

    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ITimeProvider _timeProvider;

    public DashboardService(AppDbContext context, IMemoryCache cache, ITimeProvider timeProvider)
    {
        _context = context;
        _cache = cache;
        _timeProvider = timeProvider;
    }

    public async Task<DashboardOverviewResponse> GetOverviewAsync(
        string? branchCode, string? role, CancellationToken ct = default)
    {
        // One cache entry per branch bucket; admins share "ALL". With a 15s TTL,
        // N terminals polling every 30s collapse into ≤1 compute per bucket per TTL.
        var bucket = role == Roles.Admin ? AllBranchesKey : (branchCode ?? AllBranchesKey);
        var cacheKey = $"dashboard:overview:{bucket}";

        if (_cache.TryGetValue(cacheKey, out DashboardOverviewResponse? cached) && cached is not null)
            return cached;

        var overview = await ComputeAsync(branchCode, role, ct);

        // Size = 1 is required because MemoryCache is configured with
        // SizeLimit = 10_000 in ServiceCollectionExtensions. Each dashboard
        // bucket counts as one logical unit toward that cap.
        _cache.Set(cacheKey, overview, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1,
        });
        return overview;
    }

    // ── SEQUENTIAL BY DESIGN ────────────────────────────────────────────
    // DbContext is NOT thread-safe: a scoped AppDbContext allows exactly ONE
    // operation in flight. Fanning out with Task.WhenAll on this instance
    // throws InvalidOperationException ("A second operation was started on
    // this context instance…"). Eight indexed, capped probes run in a few
    // milliseconds total and are paid once per branch per 15s TTL — there is
    // nothing to gain from parallelism here. If that ever changes, inject
    // IDbContextFactory<AppDbContext> and give each parallel query its own
    // context; never share one across threads.
    private async Task<DashboardOverviewResponse> ComputeAsync(
        string? branchCode, string? role, CancellationToken ct)
    {
        var scoped = role != Roles.Admin && !string.IsNullOrEmpty(branchCode);

        IQueryable<LoanApplication> loans = _context.LoanApplications.AsNoTracking();
        IQueryable<LoanAction> actions = _context.LoanActions.AsNoTracking();
        if (scoped)
        {
            loans = loans.Where(l => l.BranchCode == branchCode);
            actions = actions.Where(a => a.LoanApplication.BranchCode == branchCode);
        }

        var phToday = _timeProvider.UtcNow.Add(PhOffset).Date;
        var todayStartUtc = phToday.AddHours(-8);
        var yesterdayStartUtc = todayStartUtc.AddDays(-1);
        var weekStartUtc = todayStartUtc.AddDays(-6);
        var activeSinceUtc = _timeProvider.UtcNow.Add(-ActiveWindow);
        var servingSinceUtc = _timeProvider.UtcNow.Add(-ServingWindow);

        // 1 ── Pending queue (oldest first) + total.
        var pendingRows = await loans
            .Where(l => PendingStatuses.Contains(l.Status))
            .OrderBy(l => l.LastActionDate)
            .Select(l => new { l.LamId, l.BranchCode, l.Status, l.LastActionDate })
            .Take(QueueSize)
            .ToListAsync(ct);

        var pendingTotal = await loans
            .CountAsync(l => PendingStatuses.Contains(l.Status), ct);

        // 2 ── Submission delta (today vs yesterday).
        var submittedToday = await loans
            .CountAsync(l => l.ApplicationDate >= todayStartUtc
                              && l.ApplicationDate < todayStartUtc.AddDays(1), ct);
        var submittedYesterday = await loans
            .CountAsync(l => l.ApplicationDate >= yesterdayStartUtc
                              && l.ApplicationDate < todayStartUtc, ct);

        // 3 ── One scan of the week's decision actions feeds three widgets:
        //     pushbacks-today KPI, approved-today KPI + %vs-avg, and the chart.
        var weekActions = await actions
            .Where(a => a.ActionDate >= weekStartUtc
                        && (a.ToStatus == "Approved" || a.ToStatus == "ForRevision"))
            .Select(a => new { a.ActionDate, a.ToStatus })
            .ToListAsync(ct);

        // 4 ── "Now serving": officers who acted in the last hour, latest first.
        var activeRows = await actions
            .Where(a => a.ActionDate >= activeSinceUtc)
            .OrderByDescending(a => a.ActionDate)
            .Select(a => new
            {
                a.ActionByUserId,
                Name = a.ActionByUser.FirstName + " " + a.ActionByUser.LastName,
                a.ActionDate,
                LamId = a.LoanApplication.LamId,
            })
            .Take(ActiveProbeCap)
            .ToListAsync(ct);

        // 5 ── Recent pushbacks (today, falling back to the week so the widget
        //     never renders empty on a quiet morning).
        var pushList = await actions
            .Where(a => a.ToStatus == "ForRevision" && a.ActionDate >= todayStartUtc)
            .OrderByDescending(a => a.ActionDate)
            .Select(a => new
            {
                a.Comments,
                a.ActionDate,
                LamId = a.LoanApplication.LamId,
                a.LoanApplication.BranchCode,
            })
            .Take(ListSize)
            .ToListAsync(ct);

        if (pushList.Count == 0)
        {
            pushList = await actions
                .Where(a => a.ToStatus == "ForRevision" && a.ActionDate >= weekStartUtc)
                .OrderByDescending(a => a.ActionDate)
                .Select(a => new
                {
                    a.Comments,
                    a.ActionDate,
                    LamId = a.LoanApplication.LamId,
                    a.LoanApplication.BranchCode,
                })
                .Take(ListSize)
                .ToListAsync(ct);
        }

        // 6 ── Recent approvals (same today→week fallback).
        var approvedList = await actions
            .Where(a => a.ToStatus == "Approved" && a.ActionDate >= todayStartUtc)
            .OrderByDescending(a => a.ActionDate)
            .Select(a => new
            {
                FullName = a.LoanApplication.FirstName + " " + a.LoanApplication.LastName,
                LamId = a.LoanApplication.LamId,
                a.LoanApplication.BranchCode,
                a.ActionDate,
            })
            .Take(ListSize)
            .ToListAsync(ct);

        if (approvedList.Count == 0)
        {
            approvedList = await actions
                .Where(a => a.ToStatus == "Approved" && a.ActionDate >= weekStartUtc)
                .OrderByDescending(a => a.ActionDate)
                .Select(a => new
                {
                    FullName = a.LoanApplication.FirstName + " " + a.LoanApplication.LastName,
                    LamId = a.LoanApplication.LamId,
                    a.LoanApplication.BranchCode,
                    a.ActionDate,
                })
                .Take(ListSize)
                .ToListAsync(ct);
        }

        // ── Assemble (in-memory over already-bounded rows) ──────────────
        var approvedToday = weekActions.Count(a => a.ToStatus == "Approved" && a.ActionDate >= todayStartUtc);
        var pushBacksToday = weekActions.Count(a => a.ToStatus == "ForRevision" && a.ActionDate >= todayStartUtc);
        var approvedWeekTotal = weekActions.Count(a => a.ToStatus == "Approved");
        var dailyAvg = approvedWeekTotal / 7.0;
        var vsAvg = dailyAvg > 0
            ? (int)Math.Round((approvedToday - dailyAvg) / dailyAvg * 100)
            : approvedToday > 0 ? 100 : 0;

        var trendMap = weekActions
            .GroupBy(a => DayLabel(a.ActionDate))
            .ToDictionary(
                g => g.Key,
                g => new DailyTrendPointDto(
                    g.Key,
                    g.Count(a => a.ToStatus == "Approved"),
                    g.Count(a => a.ToStatus == "ForRevision")));

        var orderedTrend = Enumerable.Range(0, 7)
            .Select(offset =>
            {
                var label = phToday.AddDays(-6 + offset).ToString("ddd", CultureInfo.InvariantCulture);
                return trendMap.TryGetValue(label, out var point)
                    ? point
                    : new DailyTrendPointDto(label, 0, 0);
            })
            .ToList();

        // activeRows is ordered desc, so GroupBy preserves recency order and
        // g.First() is each officer's latest action. KPI counts ALL active
        // officers; the list shows the top five.
        var activeOfficers = activeRows.GroupBy(a => a.ActionByUserId).Select(g => g.First()).ToList();
        var nowServing = activeOfficers
            .Take(ListSize)
            .Select((a, i) => new NowServingItemDto(i + 1, a.Name, a.LamId, a.ActionDate >= servingSinceUtc))
            .ToList();

        // 8 ── Document completion queue (incomplete documents waiting on encoder).
        var docQueueRows = await _context.WorkflowQueueItems.AsNoTracking()
            .Where(i => i.Stage == QueueStage.DocumentCompletion && i.State != QueueItemState.Completed)
            .Where(i => !scoped || i.LoanApplication.BranchCode == branchCode)
            .OrderBy(i => i.PartitionKey).ThenBy(i => i.EnqueuedAt).ThenBy(i => i.Id)
            .Select(i => new
            {
                i.LoanApplicationId,
                i.LoanApplication.LamId,
                i.LoanApplication.BranchCode,
                i.EnqueuedAt,
                MissingCount = i.LoanApplication.DocumentChecklists.Count(d => d.Status == "Missing" || d.Status == "Pending"),
            })
            .Take(QueueSize)
            .ToListAsync(ct);

        var documentQueue = docQueueRows
            .Select((d, i) => new DocumentQueueItemDto(i + 1, d.LamId, d.BranchCode, d.EnqueuedAt, d.MissingCount))
            .ToList();

        return new DashboardOverviewResponse(
            new DashboardKpis(
                pendingTotal,
                submittedToday - submittedYesterday,
                activeOfficers.Count,
                pushBacksToday,
                approvedToday,
                vsAvg),
            pendingRows
                .Select((l, i) => new PendingQueueItemDto(i + 1, l.LamId, l.BranchCode, l.Status, l.LastActionDate))
                .ToList(),
            nowServing,
            pushList
                .Select((p, i) => new PushBackItemDto(
                    i + 1, p.LamId, p.BranchCode,
                    string.IsNullOrWhiteSpace(p.Comments) ? "No reason recorded" : p.Comments,
                    p.ActionDate))
                .ToList(),
            approvedList
                .Select(a => new ApprovedLoanItemDto(a.FullName, a.LamId, a.BranchCode, a.ActionDate))
                .ToList(),
            orderedTrend,
            documentQueue,
            _timeProvider.UtcNow);
    }

    /// <summary>Short weekday label in Philippine wall-clock (Mon…Sun).</summary>
    private static string DayLabel(DateTime utc) =>
        utc.Add(PhOffset).ToString("ddd", CultureInfo.InvariantCulture);
}

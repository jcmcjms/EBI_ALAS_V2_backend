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
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan ServingWindow = TimeSpan.FromMinutes(15);

    // PH has no DST, so UTC+8 is exact — day buckets align with branch wall-clock.
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

        _cache.Set(cacheKey, overview, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1,
        });
        return overview;
    }

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

        // ── PH day boundaries expressed in UTC (stored timestamps are UTC) ──
        var phToday = _timeProvider.UtcNow.Add(PhOffset).Date;
        var todayStartUtc = phToday.AddHours(-8);
        var yesterdayStartUtc = todayStartUtc.AddDays(-1);
        var weekStartUtc = todayStartUtc.AddDays(-6);
        var activeSinceUtc = _timeProvider.UtcNow.Add(-ActiveWindow);
        var servingSinceUtc = _timeProvider.UtcNow.Add(-ServingWindow);

        // 1 ── Pending queue (oldest first) + total, in two indexed probes.
        var pendingRowsTask = loans
            .Where(l => PendingStatuses.Contains(l.Status))
            .OrderBy(l => l.LastActionDate)
            .Select(l => new { l.LamId, l.BranchCode, l.Status, l.LastActionDate })
            .Take(QueueSize)
            .ToListAsync(ct);
        var pendingTotalTask = loans
            .CountAsync(l => PendingStatuses.Contains(l.Status), ct);

        // 2 ── Submission delta (today vs yesterday).
        var submittedTodayTask = loans
            .CountAsync(l => l.ApplicationDate >= todayStartUtc && l.ApplicationDate < todayStartUtc.AddDays(1), ct);
        var submittedYesterdayTask = loans
            .CountAsync(l => l.ApplicationDate >= yesterdayStartUtc && l.ApplicationDate < todayStartUtc, ct);

        // 3 ── One scan of the week's decision actions feeds THREE widgets:
        //     pushbacks-today KPI, approved-today KPI + %vs avg, and the 7-day chart.
        var weekActionsTask = actions
            .Where(a => a.ActionDate >= weekStartUtc
                        && (a.ToStatus == "Approved" || a.ToStatus == "ForRevision"))
            .Select(a => new { a.ActionDate, a.ToStatus })
            .ToListAsync(ct);

        // 4 ── "Now serving": officers who acted in the last hour, latest first.
        var activeTask = actions
            .Where(a => a.ActionDate >= activeSinceUtc)
            .OrderByDescending(a => a.ActionDate)
            .Select(a => new
            {
                a.ActionByUserId,
                Name = a.ActionByUser.FirstName + " " + a.ActionByUser.LastName,
                a.ActionDate,
                LamId = a.LoanApplication.LamId,
            })
            .Take(200)   // cap the in-memory grouping; 200 events/hour/branch is far beyond real load
            .ToListAsync(ct);

        // 5 ── Recent pushbacks & approvals lists (today, falling back to the week
        //     so the widgets never render empty on a quiet morning).
        var pushListTask = actions
            .Where(a => a.ToStatus == "ForRevision" && a.ActionDate >= todayStartUtc)
            .OrderByDescending(a => a.ActionDate)
            .Select(a => new { a.Comments, a.ActionDate, LamId = a.LoanApplication.LamId, a.LoanApplication.BranchCode })
            .Take(ListSize)
            .ToListAsync(ct);
        var approvedListTask = actions
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

        // Note: tasks are awaited individually below, not via Task.WhenAll.
        // EF Core's DbContext is not thread-safe; concurrent operations on
        // the same instance throw
        //   "A second operation was started on this context instance…"
        // The eight probes are all indexed seeks + TOP-N, so the total
        // sequential cost (~8 ms) is negligible next to the 15 s cache TTL.

        // ── Assemble (all in-memory over already-bounded rows) ───────────────
        var pendingRows = await pendingRowsTask;
        var pendingTotal = await pendingTotalTask;
        var submittedToday = await submittedTodayTask;
        var submittedYesterday = await submittedYesterdayTask;
        var weekActions = await weekActionsTask;
        var active = (await activeTask)
            .GroupBy(a => a.ActionByUserId)
            .Select(g => g.First())          // already ordered desc → first = latest per officer
            .Take(ListSize)
            .Select((a, i) => new NowServingItemDto(i + 1, a.Name, a.LamId, a.ActionDate >= servingSinceUtc))
            .ToList();
        var pushList = (await pushListTask).ToList();
        var approvedList = (await approvedListTask).ToList();

        var approvedToday = weekActions.Count(a => a.ToStatus == "Approved" && a.ActionDate >= todayStartUtc);
        var pushBacksToday = weekActions.Count(a => a.ToStatus == "ForRevision" && a.ActionDate >= todayStartUtc);
        var approvedWeekTotal = weekActions.Count(a => a.ToStatus == "Approved");
        var dailyAvg = approvedWeekTotal / 7.0;
        var vsAvg = dailyAvg > 0
            ? (int)Math.Round((approvedToday - dailyAvg) / dailyAvg * 100)
            : approvedToday > 0 ? 100 : 0;

        var trend = weekActions
            .GroupBy(a => DayLabel(a.ActionDate))
            .Select(g => new DailyTrendPointDto(g.Key,
                g.Count(a => a.ToStatus == "Approved"),
                g.Count(a => a.ToStatus == "ForRevision")))
            .ToList();
        // Guarantee 7 ordered buckets even for days with zero activity.
        var trendMap = trend.ToDictionary(t => t.Day);
        var orderedTrend = Enumerable.Range(0, 7)
            .Select(offset =>
            {
                var label = DayLabel(todayStartUtc.AddDays(-6 + offset).AddMinutes(1));
                return trendMap.TryGetValue(label, out var p)
                    ? p
                    : new DailyTrendPointDto(label, 0, 0);
            })
            .ToList();

        // Quiet-morning fallbacks: if today's list is empty (PH business hours
        // haven't started, or a weekend), widen to the week's most recent so
        // the widget never renders an empty panel.
        if (pushList.Count == 0)
        {
            pushList = await _context.LoanActions.AsNoTracking()
                .Where(a => a.ToStatus == "ForRevision" && a.ActionDate >= weekStartUtc
                            && (!scoped || a.LoanApplication.BranchCode == branchCode))
                .OrderByDescending(a => a.ActionDate)
                .Select(a => new { a.Comments, a.ActionDate, LamId = a.LoanApplication.LamId, a.LoanApplication.BranchCode })
                .Take(ListSize)
                .ToListAsync(ct);
        }
        if (approvedList.Count == 0)
        {
            approvedList = await _context.LoanActions.AsNoTracking()
                .Where(a => a.ToStatus == "Approved" && a.ActionDate >= weekStartUtc
                            && (!scoped || a.LoanApplication.BranchCode == branchCode))
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

        return new DashboardOverviewResponse(
            new DashboardKpis(
                pendingTotal,
                submittedToday - submittedYesterday,
                active.Count,
                pushBacksToday,
                approvedToday,
                vsAvg),
            pendingRows
                .Select((l, i) => new PendingQueueItemDto(i + 1, l.LamId, l.BranchCode, l.Status, l.LastActionDate))
                .ToList(),
            active,
            pushList
                .Select((p, i) => new PushBackItemDto(i + 1, p.LamId, p.BranchCode,
                    string.IsNullOrWhiteSpace(p.Comments) ? "No reason recorded" : p.Comments, p.ActionDate))
                .ToList(),
            approvedList
                .Select(a => new ApprovedLoanItemDto(a.FullName, a.LamId, a.BranchCode, a.ActionDate))
                .ToList(),
            orderedTrend,
            _timeProvider.UtcNow);
    }

    /// <summary>Short weekday label in Philippine wall-clock (Mon…Sun).</summary>
    private static string DayLabel(DateTime utc) =>
        utc.Add(PhOffset).ToString("ddd", System.Globalization.CultureInfo.InvariantCulture);
}

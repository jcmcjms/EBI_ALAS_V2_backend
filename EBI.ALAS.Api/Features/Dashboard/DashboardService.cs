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

        // Statuses where a human action is still owed — this IS the "pending" queue.
        var pendingStatuses = PendingStatuses;

        // 1 ── Pending queue (oldest first) + total.
        var pendingRows = await loans
            .Where(l => pendingStatuses.Contains(l.Status))
            .OrderBy(l => l.LastActionDate)
            .Select(l => new
            {
                l.LamId, l.BranchCode, l.Status, l.LastActionDate,
                ClientName = l.FirstName + " " + l.LastName,
                EncoderName = l.CreatedBy.FirstName + " " + l.CreatedBy.LastName,
            })
            .Take(QueueSize)
            .ToListAsync(ct);

        var pendingTotal = await loans
            .CountAsync(l => pendingStatuses.Contains(l.Status), ct);

        // 2 ── Submission delta (today vs yesterday).
        var submittedToday = await loans
            .CountAsync(l => l.ApplicationDate >= todayStartUtc
                              && l.ApplicationDate < todayStartUtc.AddDays(1), ct);
        var submittedYesterday = await loans
            .CountAsync(l => l.ApplicationDate >= yesterdayStartUtc
                              && l.ApplicationDate < todayStartUtc, ct);

        // 3 ── One scan of the week's decision actions feeds three widgets:
        //     pushbacks-today KPI, approved-today KPI + %vs-avg, and the chart.
        // SQL GROUP BY instead of materializing 35K rows into memory.
        // The original code loaded every Approved/ForRevision action for 7 days
        // and grouped in C#. At ~5,000 decisions/day that's 35,000 rows every 15s.
        // Now SQL Server returns only ~14 aggregated rows (7 days × 2 statuses).
        // Use raw SQL DATEADD for PH timezone grouping — EF Core's DateTime.Add()
        // cannot translate to SQL Server's DATEADD, so we use SqlQuery with FormattableString.
        // Two separate FormattableStrings ensure proper SQL parameterization for branchCode.
        var weekActionGroups = scoped
            ? await _context.Database.SqlQuery<WeekActionGroupRow>($"""
                SELECT
                    CAST(DATEADD(hour, 8, a.ActionDate) AS date) AS DayLabel,
                    a.ToStatus,
                    COUNT(*) AS [Count]
                FROM LoanActions a
                INNER JOIN LoanApplications la ON la.Id = a.LoanApplicationId
                WHERE a.ActionDate >= {weekStartUtc}
                  AND (a.ToStatus = 'Approved' OR a.ToStatus = 'ForRevision')
                  AND la.BranchCode = {branchCode}
                GROUP BY CAST(DATEADD(hour, 8, a.ActionDate) AS date), a.ToStatus
                """).ToListAsync(ct)
            : await _context.Database.SqlQuery<WeekActionGroupRow>($"""
                SELECT
                    CAST(DATEADD(hour, 8, a.ActionDate) AS date) AS DayLabel,
                    a.ToStatus,
                    COUNT(*) AS [Count]
                FROM LoanActions a
                INNER JOIN LoanApplications la ON la.Id = a.LoanApplicationId
                WHERE a.ActionDate >= {weekStartUtc}
                  AND (a.ToStatus = 'Approved' OR a.ToStatus = 'ForRevision')
                GROUP BY CAST(DATEADD(hour, 8, a.ActionDate) AS date), a.ToStatus
                """).ToListAsync(ct);

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

        // Use aggregated weekActionGroups (14 rows) instead of raw weekActions (35K rows).
        var approvedToday = weekActionGroups
            .Where(g => g.ToStatus == "Approved" && g.DayLabel >= phToday)
            .Sum(g => g.Count);
        var pushBacksToday = weekActionGroups
            .Where(g => g.ToStatus == "ForRevision" && g.DayLabel >= phToday)
            .Sum(g => g.Count);
        var approvedWeekTotal = weekActionGroups
            .Where(g => g.ToStatus == "Approved")
            .Sum(g => g.Count);
        var dailyAvg = approvedWeekTotal / 7.0;
        var vsAvg = dailyAvg > 0
            ? (int)Math.Round((approvedToday - dailyAvg) / dailyAvg * 100)
            : approvedToday > 0 ? 100 : 0;

        // Build trend map from aggregated groups (no per-row grouping needed).
        var trendMap = weekActionGroups
            .GroupBy(g => g.DayLabel.ToString("ddd", CultureInfo.InvariantCulture))
            .ToDictionary(
                g => g.Key,
                g => new DailyTrendPointDto(
                    g.Key,
                    g.Where(x => x.ToStatus == "Approved").Sum(x => x.Count),
                    g.Where(x => x.ToStatus == "ForRevision").Sum(x => x.Count)));

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

        // 8 ── Document flag queue (flagged files waiting on encoder).
        //     Document deficiency is a data flag, not a status. Flagged files
        //     stay at their real desk — this widget surfaces them by flag columns.
        var docQueueRows = await loans
            .Where(l => l.DocumentsFlaggedAt != null)
            .OrderBy(l => l.DocumentsFlaggedAt)
            .Select(l => new
            {
                l.Id,
                l.LamId,
                l.BranchCode,
                ClientName = l.FirstName + " " + l.LastName,
                EncoderName = l.CreatedBy.FirstName + " " + l.CreatedBy.LastName,
                l.LastActionDate,
                l.Status,
                MissingCount = l.DocumentChecklists.Count(d => d.Status == "Missing" || d.Status == "Pending"),
                l.DocumentsFlaggedAt,
                FlaggedByName = l.DocumentsFlaggedBy != null
                    ? l.DocumentsFlaggedBy.FirstName + " " + l.DocumentsFlaggedBy.LastName
                    : null,
            })
            .Take(QueueSize)
            .ToListAsync(ct);

        var documentQueue = docQueueRows
            .Select((d, i) => new DocumentQueueItemDto(d.Id, i + 1, d.LamId, d.BranchCode,
                d.DocumentsFlaggedAt ?? d.LastActionDate, d.MissingCount, d.ClientName,
                d.EncoderName, d.FlaggedByName, d.DocumentsFlaggedAt))
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
                .Select((l, i) => new PendingQueueItemDto(i + 1, l.LamId, l.BranchCode, l.Status, l.LastActionDate, l.ClientName, l.EncoderName))
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

/// <summary>Projection for raw SQL GROUP BY with DATEADD (PH timezone).</summary>
public sealed class WeekActionGroupRow
{
    public DateTime DayLabel { get; set; }
    public string ToStatus { get; set; } = default!;
    public int Count { get; set; }
}

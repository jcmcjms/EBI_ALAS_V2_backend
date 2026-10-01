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
    private static readonly string[] PendingStatuses =
        ["ForRecommendation", "ForChecking", "ForApproval", "ForRevision"];
    private const int QueueSize = 6;
    private const int ListSize = 5;
    private const int ActiveProbeCap = 200;
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan ServingWindow = TimeSpan.FromMinutes(15);
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
        var phToday = _timeProvider.UtcNow.Add(PhOffset).Date;
        var todayStartUtc = phToday.AddHours(-8);
        var yesterdayStartUtc = todayStartUtc.AddDays(-1);
        var weekStartUtc = todayStartUtc.AddDays(-6);
        var activeSinceUtc = _timeProvider.UtcNow.Add(-ActiveWindow);
        var pendingStatuses = PendingStatuses;
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
        var submittedToday = await loans
            .CountAsync(l => l.ApplicationDate >= todayStartUtc
                              && l.ApplicationDate < todayStartUtc.AddDays(1), ct);
        var submittedYesterday = await loans
            .CountAsync(l => l.ApplicationDate >= yesterdayStartUtc
                              && l.ApplicationDate < todayStartUtc, ct);
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
        IQueryable<WorkflowQueueItem> queueItems = _context.WorkflowQueueItems.AsNoTracking();
        if (scoped)
        {
            queueItems = queueItems.Where(i => i.LoanApplication.BranchCode == branchCode);
        }
        var servingSinceUtc = _timeProvider.UtcNow.AddMinutes(-60);
        var nowServingItems = await queueItems
            .Where(i => i.State == QueueItemState.Active
                        && i.OwnerUserId != null
                        && i.LeasedAt != null
                        && i.LeasedAt >= servingSinceUtc)
            .OrderByDescending(i => i.LeasedAt)
            .Take(ListSize)
            .Select(i => new
            {
                i.OwnerUserId,
                OwnerName = i.OwnerUser != null
                    ? i.OwnerUser.FirstName + " " + i.OwnerUser.LastName
                    : null,
                LamId = i.LoanApplication != null ? i.LoanApplication.LamId : "",
                i.LeasedAt,
            })
            .ToListAsync(ct);
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
        var activeOfficers = activeRows.GroupBy(a => a.ActionByUserId).Select(g => g.First()).ToList();
        var nowServing = nowServingItems
            .Select((s, i) => new NowServingItemDto(
                i + 1,
                s.OwnerName ?? "Reviewer",
                s.LamId,
                true))
            .ToList();
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
                nowServingItems.Count,
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
    private static string DayLabel(DateTime utc) =>
        utc.Add(PhOffset).ToString("ddd", CultureInfo.InvariantCulture);
}
public sealed class WeekActionGroupRow
{
    public DateTime DayLabel { get; set; }
    public string ToStatus { get; set; } = default!;
    public int Count { get; set; }
}

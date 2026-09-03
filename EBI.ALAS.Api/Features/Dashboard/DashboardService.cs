using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EBI.ALAS.Api.Features.Dashboard;
public class DashboardService : IDashboardService
{
    // One cache key per (branch, role) bucket. Admin role is keyed under
    // the literal "ALL" because admin sees every branch.
    private static readonly TimeSpan SummaryTtl = TimeSpan.FromMinutes(2);
    private const string AllBranchesKey = "ALL";

    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(
        AppDbContext context,
        IMemoryCache cache,
        ILogger<DashboardService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task<DashboardSummaryResponse> GetSummaryAsync(string? branchId = null, string? role = null)
    {
        // Admins share a single cache entry (no branch filter); everyone
        // else gets a per-branch entry. This keeps the cache small under
        // a 3000-user load where most non-admin users hit the same branch
        // summary repeatedly.
        var effectiveBranch = role == Roles.Admin ? AllBranchesKey : (branchId ?? AllBranchesKey);
        var cacheKey = $"dashboard:summary:{effectiveBranch}";

        if (_cache.TryGetValue(cacheKey, out DashboardSummaryResponse? cached) && cached is not null)
        {
            return cached;
        }

        var summary = await ComputeSummaryAsync(branchId, role);

        // Absolute expiry so a status change anywhere in the system
        // surfaces within two minutes. No sliding window: the dashboard
        // is supposed to *approximate* reality, not race every write.
        _cache.Set(cacheKey, summary, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = SummaryTtl,
            Size = 1
        });

        _logger.LogDebug("Dashboard summary cached for branch {Branch} (TTL={Ttl}s)", effectiveBranch, SummaryTtl.TotalSeconds);
        return summary;
    }

    private async Task<DashboardSummaryResponse> ComputeSummaryAsync(string? branchId, string? role)
    {
        var query = _context.LoanApplications.AsQueryable();

        if (!string.IsNullOrEmpty(branchId) && role != Roles.Admin)
        {
            query = query.Where(l => l.BranchCode == branchId);
        }

        // Fan out: 4 independent aggregates against the same query shape.
        // EF Core will run them on the same connection (concurrent
        // execution is throttled by the underlying pool) and we get one
        // round-trip per aggregation rather than five separate requests.
        var statusCountsTask = query
            .GroupBy(l => l.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var branchCountsTask = query
            .GroupBy(l => l.BranchCode)
            .Select(g => new
            {
                BranchCode = g.Key,
                ApplicationCount = g.Count(),
                TotalAmount = g.Sum(l => l.ProposedAmount)
            })
            .ToListAsync();

        var totalCountTask = query.CountAsync();
        var totalAmountTask = query.SumAsync(l => l.ProposedAmount);

        await Task.WhenAll(statusCountsTask, branchCountsTask, totalCountTask, totalAmountTask);

        var statusCounts = await statusCountsTask;
        var branchCounts = await branchCountsTask;

        return new DashboardSummaryResponse
        {
            TotalApplications = await totalCountTask,
            TotalAmount = await totalAmountTask,
            StatusCounts = statusCounts.ToDictionary(x => x.Status, x => x.Count),
            BranchCounts = branchCounts.ToDictionary(
                x => x.BranchCode,
                x => new BranchSummary
                {
                    BranchCode = x.BranchCode,
                    ApplicationCount = x.ApplicationCount,
                    TotalAmount = x.TotalAmount
                })
        };
    }
}
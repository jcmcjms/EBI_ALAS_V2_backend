using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Dashboard;

/// <summary>
/// Dashboard service implementation for aggregating loan statistics.
/// Uses database-side aggregation to avoid loading large datasets into memory.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly AppDbContext _context;

    public DashboardService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardSummaryResponse> GetSummaryAsync(string? branchId = null, string? role = null)
    {
        var query = _context.LoanApplications.AsQueryable();

        // Apply branch filtering for non-admin users
        if (!string.IsNullOrEmpty(branchId) && role != Roles.Admin)
        {
            query = query.Where(l => l.BranchCode == branchId);
        }

        // Use database-side aggregation instead of loading all rows into memory
        // Execute two queries: one for status counts, one for branch counts
        var statusCounts = await query
            .GroupBy(l => l.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var branchCounts = await query
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

        await Task.WhenAll(totalCountTask, totalAmountTask);

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

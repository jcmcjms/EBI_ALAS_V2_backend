using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Dashboard;

/// <summary>
/// Dashboard service implementation for aggregating loan statistics.
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

        var loans = await query.ToListAsync();

        var statusCounts = loans
            .GroupBy(l => l.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        var branchCounts = loans
            .GroupBy(l => l.BranchCode)
            .ToDictionary(
                g => g.Key,
                g => new BranchSummary
                {
                    BranchCode = g.Key,
                    ApplicationCount = g.Count(),
                    TotalAmount = g.Sum(l => l.ProposedAmount)
                });

        return new DashboardSummaryResponse
        {
            TotalApplications = loans.Count,
            TotalAmount = loans.Sum(l => l.ProposedAmount),
            StatusCounts = statusCounts,
            BranchCounts = branchCounts
        };
    }
}

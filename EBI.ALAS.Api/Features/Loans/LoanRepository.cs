using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;
public class LoanRepository : ILoanRepository
{
    private readonly AppDbContext _context;

    public LoanRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<LoanApplication?> GetByIdAsync(int id, bool includeRelated = false, CancellationToken ct = default)
    {
        var query = _context.LoanApplications
            .Where(l => l.Id == id);

        if (includeRelated)
        {
            query = query
                .Include(l => l.CreatedBy)
                .Include(l => l.Actions)
                    .ThenInclude(a => a.ActionByUser)
                .Include(l => l.OutstandingLoans)
                .Include(l => l.BuyOuts);
        }

        return await query.FirstOrDefaultAsync(ct);
    }

    public async Task<LoanApplication?> GetByFormNumberAsync(string formNumber, CancellationToken ct = default)
    {
        return await _context.LoanApplications
            .FirstOrDefaultAsync(l => l.FormNumber == formNumber, ct);
    }

    public async Task<PagedResult<LoanApplication>> GetAllAsync(
        int page,
        int pageSize,
        string? role = null,
        string? branchId = null,
        int? userId = null,
        bool includeRelated = false,
        CancellationToken ct = default)
    {
        var query = _context.LoanApplications.AsQueryable();

        // Apply role-based filtering BEFORE includes so the join does not
        // multiply row counts unnecessarily.
        if (!string.IsNullOrEmpty(role))
        {
            query = role switch
            {
                Roles.Encoder when userId.HasValue =>
                    query.Where(l => l.CreatedById == userId.Value),
                Roles.Recommender =>
                    query.Where(l => l.Status == "ForRecommendation"),
                Roles.Evaluator =>
                    query.Where(l => l.Status == "ForChecking"),
                Roles.Approver =>
                    query.Where(l => l.Status == "ForApproval"),
                _ => query // Admin sees all
            };
        }

        // Apply branch filtering
        if (!string.IsNullOrEmpty(branchId) && role != Roles.Admin)
        {
            query = query.Where(l => l.BranchCode == branchId);
        }

        // OPTIONAL related-data hydration. The list endpoint defaults to a
        // lightweight projection (created-by only) because every caller
        // was previously triggering N+1 follow-up queries the moment they
        // touched l.Actions / l.OutstandingLoans / l.BuyOuts. Pass
        // includeRelated=true only from flows that genuinely need the
        // full aggregate (the loan detail screen, the audit reviewer,
        // etc.).
        if (includeRelated)
        {
            query = query
                .Include(l => l.CreatedBy)
                .Include(l => l.Actions)
                    .ThenInclude(a => a.ActionByUser)
                .Include(l => l.OutstandingLoans)
                .Include(l => l.BuyOuts);
        }
        else
        {
            // Always include CreatedBy — the list view renders it.
            query = query.Include(l => l.CreatedBy);
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(l => l.ApplicationDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PagedResult<LoanApplication>.Create(items, totalCount, page, pageSize);
    }

    public async Task<LoanApplication> CreateAsync(LoanApplication loan, CancellationToken ct = default)
    {
        _context.LoanApplications.Add(loan);
        await _context.SaveChangesAsync(ct);
        return loan;
    }

    public async Task UpdateAsync(LoanApplication loan, CancellationToken ct = default)
    {
        _context.LoanApplications.Update(loan);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken ct = default)
    {
        return await _context.LoanApplications.AnyAsync(l => l.Id == id, ct);
    }

    public async Task<int> GetCountByStatusAsync(string status, string? branchId = null, CancellationToken ct = default)
    {
        var query = _context.LoanApplications
            .Where(l => l.Status == status);

        if (!string.IsNullOrEmpty(branchId))
        {
            query = query.Where(l => l.BranchCode == branchId);
        }

        return await query.CountAsync(ct);
    }

    public async Task<decimal> GetTotalAmountByStatusAsync(string status, string? branchId = null, CancellationToken ct = default)
    {
        var query = _context.LoanApplications
            .Where(l => l.Status == status);

        if (!string.IsNullOrEmpty(branchId))
        {
            query = query.Where(l => l.BranchCode == branchId);
        }

        return await query.SumAsync(l => l.ProposedAmount, ct);
    }
}
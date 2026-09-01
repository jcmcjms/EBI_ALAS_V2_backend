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

    public async Task<LoanApplication?> GetByIdAsync(int id, bool includeRelated = false)
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

        return await query.FirstOrDefaultAsync();
    }

    public async Task<LoanApplication?> GetByFormNumberAsync(string formNumber)
    {
        return await _context.LoanApplications
            .FirstOrDefaultAsync(l => l.FormNumber == formNumber);
    }

    public async Task<PagedResult<LoanApplication>> GetAllAsync(
        int page,
        int pageSize,
        string? role = null,
        string? branchId = null,
        int? userId = null)
    {
        var query = _context.LoanApplications
            .Include(l => l.CreatedBy)
            .AsQueryable();

        // Apply role-based filtering
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

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(l => l.ApplicationDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return PagedResult<LoanApplication>.Create(items, totalCount, page, pageSize);
    }

    public async Task<LoanApplication> CreateAsync(LoanApplication loan)
    {
        _context.LoanApplications.Add(loan);
        await _context.SaveChangesAsync();
        return loan;
    }

    public async Task UpdateAsync(LoanApplication loan)
    {
        _context.LoanApplications.Update(loan);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await _context.LoanApplications.AnyAsync(l => l.Id == id);
    }

    public async Task<int> GetCountByStatusAsync(string status, string? branchId = null)
    {
        var query = _context.LoanApplications
            .Where(l => l.Status == status);

        if (!string.IsNullOrEmpty(branchId))
        {
            query = query.Where(l => l.BranchCode == branchId);
        }

        return await query.CountAsync();
    }

    public async Task<decimal> GetTotalAmountByStatusAsync(string status, string? branchId = null)
    {
        var query = _context.LoanApplications
            .Where(l => l.Status == status);

        if (!string.IsNullOrEmpty(branchId))
        {
            query = query.Where(l => l.BranchCode == branchId);
        }

        return await query.SumAsync(l => l.ProposedAmount);
    }
}

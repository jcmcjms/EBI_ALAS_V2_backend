using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Shared.Models;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace EBI.ALAS.Api.Features.Loans;
public class LoanRepository(AppDbContext context) : ILoanRepository
{
    private static readonly Func<AppDbContext, int, Task<LoanApplication?>> GetLoanByIdWithRelatedCompiled =
        EF.CompileAsyncQuery((AppDbContext db, int id) =>
            db.LoanApplications
                .AsNoTracking()
                .AsSplitQuery()
                .Include(l => l.CreatedBy)
                .Include(l => l.Actions)
                    .ThenInclude(a => a.ActionByUser)
                .Include(l => l.OutstandingLoans)
                .Include(l => l.BuyOuts)
                .Include(l => l.EbiReloans)
                .Include(l => l.IncomingLoans)
                .FirstOrDefault(l => l.Id == id));
    private static readonly Func<AppDbContext, int, Task<LoanApplication?>> GetLoanByIdTrackedCompiled =
        EF.CompileAsyncQuery((AppDbContext db, int id) =>
            db.LoanApplications
                .Include(l => l.Deviations)
                .FirstOrDefault(l => l.Id == id));
    private static readonly Func<AppDbContext, string, Task<LoanApplication?>> GetLoanByLamIdCompiled =
        EF.CompileAsyncQuery((AppDbContext db, string lamId) =>
            db.LoanApplications
                .FirstOrDefault(l => l.LamId == lamId));
    private static readonly Func<AppDbContext, string, Task<LoanApplication?>> GetLoanByLoanNoCompiled =
        EF.CompileAsyncQuery((AppDbContext db, string loanNo) =>
            db.LoanApplications
                .FirstOrDefault(l => l.LoanNo == loanNo));
    private static readonly Func<AppDbContext, int, Task<bool>> LoanExistsCompiled =
        EF.CompileAsyncQuery((AppDbContext db, int id) =>
            db.LoanApplications.Any(l => l.Id == id));
    private static readonly Func<AppDbContext, string, string?, Task<int>> CountByStatusCompiled =
        EF.CompileAsyncQuery((AppDbContext db, string status, string? branchId) =>
            db.LoanApplications
                .Where(l => l.Status == status && (branchId == null || l.BranchCode == branchId))
                .Count());
    public async Task<LoanApplication?> GetByIdAsync(int id, bool includeRelated = false, CancellationToken ct = default)
    {
        if (includeRelated)
        {
            return await GetLoanByIdWithRelatedCompiled(context, id);
        }
        return await GetLoanByIdTrackedCompiled(context, id);
    }
    public async Task<LoanApplication?> GetByLamIdAsync(string lamId, CancellationToken ct = default)
    {
        return await GetLoanByLamIdCompiled(context, lamId);
    }
    public async Task<LoanApplication?> GetByLoanNoAsync(string loanNo, CancellationToken ct = default)
    {
        return await GetLoanByLoanNoCompiled(context, loanNo);
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
        var query = context.LoanApplications.AsQueryable();
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
                _ => query
            };
        }
        if (!string.IsNullOrEmpty(branchId) && role != Roles.Admin)
        {
            query = query.Where(l => l.BranchCode == branchId);
        }
        if (includeRelated)
        {
            query = query
                .AsNoTracking()
                .AsSplitQuery()
                .Include(l => l.CreatedBy)
                .Include(l => l.Actions)
                    .ThenInclude(a => a.ActionByUser)
                .Include(l => l.OutstandingLoans)
                .Include(l => l.BuyOuts)
                .Include(l => l.EbiReloans)
                .Include(l => l.IncomingLoans);
        }
        else
        {
            query = query
                .AsNoTracking()
                .Include(l => l.CreatedBy);
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
        context.LoanApplications.Add(loan);
        await context.SaveChangesAsync(ct);
        return loan;
    }
    public async Task UpdateAsync(LoanApplication loan, CancellationToken ct = default)
    {
        context.LoanApplications.Update(loan);
        await context.SaveChangesAsync(ct);
    }
    public void TrackUpdate(LoanApplication loan)
    {
        context.LoanApplications.Update(loan);
    }
    public async Task<bool> ExistsAsync(int id, CancellationToken ct = default)
    {
        return await LoanExistsCompiled(context, id);
    }
    public async Task<int> GetCountByStatusAsync(string status, string? branchId = null, CancellationToken ct = default)
    {
        return await CountByStatusCompiled(context, status, branchId);
    }
    public async Task<decimal> GetTotalAmountByStatusAsync(string status, string? branchId = null, CancellationToken ct = default)
    {
        var query = context.LoanApplications
            .Where(l => l.Status == status);
        if (!string.IsNullOrEmpty(branchId))
        {
            query = query.Where(l => l.BranchCode == branchId);
        }
        return await query.SumAsync(l => l.ProposedAmount, ct);
    }
    public async Task<string?> GetOfficerDisplayNameAsync(int userId, CancellationToken ct = default)
    {
        var name = await context.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.FirstName, u.MiddleName, u.LastName })
            .FirstOrDefaultAsync(ct);
        if (name is null) return null;
        var middle = string.IsNullOrWhiteSpace(name.MiddleName)
            ? string.Empty
            : $"{char.ToUpperInvariant(name.MiddleName.Trim()[0])}.";
        return middle.Length == 0
            ? $"{name.FirstName} {name.LastName}"
            : $"{name.FirstName} {middle} {name.LastName}";
    }
    public async Task<LoanSubmissionIdempotency?> GetIdempotencyRecordAsync(
        Guid key, int userId, CancellationToken ct = default)
    {
        return await context.LoanSubmissionIdempotencies
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.IdempotencyKey == key && r.UserId == userId, ct);
    }
    public async Task CreateSubmissionAsync(
        IReadOnlyList<LoanApplication> applications,
        LoanSubmissionIdempotency idempotency,
        CancellationToken ct = default)
    {
        context.LoanApplications.AddRange(applications);
        context.LoanSubmissionIdempotencies.Add(idempotency);
        await context.SaveChangesAsync(ct);
    }
    public void TrackSubmission(
        IReadOnlyList<LoanApplication> applications,
        LoanSubmissionIdempotency idempotency)
    {
        context.LoanApplications.AddRange(applications);
        context.LoanSubmissionIdempotencies.Add(idempotency);
    }
    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await context.SaveChangesAsync(ct);
    }
    public async Task UpdateIdempotencyResponseAsync(
        LoanSubmissionIdempotency idempotency, CancellationToken ct = default)
    {
        context.LoanSubmissionIdempotencies.Update(idempotency);
        await context.SaveChangesAsync(ct);
    }
    public async Task<List<User>> GetUsersByRoleAndBranchAsync(
        string role, string branchId, CancellationToken ct = default)
    {
        return await context.Users
            .Where(u => u.Role == role && u.BranchId == branchId && u.IsActive)
            .ToListAsync(ct);
    }
    public async Task<User?> GetUserByIdAsync(int userId, CancellationToken ct = default)
    {
        return await context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
    }
}

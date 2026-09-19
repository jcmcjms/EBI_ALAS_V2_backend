using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

public class LoanRepository(AppDbContext context) : ILoanRepository
{

    // ── Compiled Queries for Hot Paths ──────────────────────────────────
    // EF compiled queries skip the expression-tree visit on every call.
    // For high-traffic endpoints (loan detail, list), this shaves ~0.5ms
    // per invocation and avoids repeated plan-cache lookups in SQL Server.
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
                .FirstOrDefault(l => l.Id == id));

    private static readonly Func<AppDbContext, string, Task<LoanApplication?>> GetLoanByLamIdCompiled =
        EF.CompileAsyncQuery((AppDbContext db, string lamId) =>
            db.LoanApplications
                .FirstOrDefault(l => l.LamId == lamId));

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
            // Use compiled query for the hot path (loan detail screen)
            return await GetLoanByIdWithRelatedCompiled(context, id);
        }

        // Tracked query for mutation paths (status update, cancel)
        return await GetLoanByIdTrackedCompiled(context, id);
    }

    public async Task<LoanApplication?> GetByLamIdAsync(string lamId, CancellationToken ct = default)
    {
        return await GetLoanByLamIdCompiled(context, lamId);
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
            // Same split-query + no-tracking pattern as GetByIdAsync.
            // Prevents cartesian explosion on the list endpoint when
            // callers request the full aggregate.
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
        context.LoanApplications.Add(loan);
        await context.SaveChangesAsync(ct);
        return loan;
    }

    public async Task UpdateAsync(LoanApplication loan, CancellationToken ct = default)
    {
        context.LoanApplications.Update(loan);
        await context.SaveChangesAsync(ct);
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

    // ── Multi-loan submission ─────────────────────────────────────────

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

    public async Task UpdateIdempotencyResponseAsync(
        LoanSubmissionIdempotency idempotency, CancellationToken ct = default)
    {
        context.LoanSubmissionIdempotencies.Update(idempotency);
        await context.SaveChangesAsync(ct);
    }

    // ── Notification routing helpers ─────────────────────────────────────

    public async Task<List<User>> GetUsersByRoleAndBranchAsync(
        string role, string branchId, CancellationToken ct = default)
    {
        // IsActive filter — never notify suspended users. The branch
        // match uses User.BranchId (== Branch.Code per the auth contract,
        // see ClaimsPrincipalExtensions.GetBranchCode). Both columns are
        // indexed via the standard Users indexes, so the lookup is a
        // single seek even with thousands of users.
        return await context.Users
            .Where(u => u.Role == role && u.BranchId == branchId && u.IsActive)
            .ToListAsync(ct);
    }
}
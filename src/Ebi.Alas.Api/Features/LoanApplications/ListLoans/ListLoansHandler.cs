using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Pagination;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications.ListLoans;

public sealed class ListLoansHandler(AlasDbContext db)
{
    public static IReadOnlyList<LoanStatus> ParseStatuses(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return [];
        }

        return status
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Enum.TryParse<LoanStatus>(s, ignoreCase: true, out var v) ? (LoanStatus?)v : null)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .ToList();
    }

    public async Task<PageResult<LoanResponse>> HandleAsync(
        PageRequest page,
        int maxPageSize,
        int defaultPageSize,
        IReadOnlyList<LoanStatus> statuses,
        string? branchId,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        var normalized = page.Normalize(maxPageSize, defaultPageSize);
        var query = db.LoanApplications.AsNoTracking().AsQueryable();

        if (!caller.CanAccessAllBranches)
        {
            query = query.Where(l => l.BranchId == caller.BranchId);
        }
        else if (!string.IsNullOrWhiteSpace(branchId))
        {
            query = query.Where(l => l.BranchId == branchId);
        }

        if (statuses.Count > 0)
        {
            query = query.Where(l => statuses.Contains(l.Status));
        }

        var ordered = query.OrderByDescending(l => l.CreatedAt).ThenBy(l => l.LamId);
        var total = await ordered.CountAsync(cancellationToken);
        var rows = await ordered
            .Skip(normalized.Skip)
            .Take(normalized.PageSize)
            .Select(l => new
            {
                l.Id,
                l.LamId,
                l.ApplicationGroupNo,
                l.ClientName,
                l.BranchId,
                LoanType = l.LoanType.ToString(),
                Status = l.Status.ToString(),
                l.Principal,
                l.TermDays,
                l.InterestRate,
                l.TotalInterest,
                l.TotalDeductions,
                l.NetProceeds,
                l.CreatedAt
            })
            .ToListAsync(cancellationToken);

        List<LoanResponse> items = [.. rows.Select(r => new LoanResponse(
            r.Id,
            r.LamId,
            r.ApplicationGroupNo,
            r.ClientName,
            r.BranchId,
            r.LoanType,
            r.Status,
            r.Principal,
            r.TermDays,
            r.InterestRate,
            r.TotalInterest,
            r.TotalDeductions,
            r.NetProceeds,
            r.CreatedAt))];

        return new PageResult<LoanResponse>(items, total, normalized.Page, normalized.PageSize);
    }
}

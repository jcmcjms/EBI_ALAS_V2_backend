using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications;

/// <summary>
/// Branch isolation for loan reads/writes. Non-admin callers only see their home branch.
/// </summary>
public static class LoanAccess
{
    public static async Task<LoanApplication> RequireAccessibleAsync(
        AlasDbContext db,
        Guid loanId,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        var query = db.LoanApplications.AsNoTracking().Where(l => l.Id == loanId);
        if (!caller.CanViewAllLoanBranches)
        {
            query = query.Where(l => l.BranchId == caller.BranchId);
        }

        return await query.FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Loan", loanId.ToString());
    }

    public static async Task<bool> CanAccessAsync(
        AlasDbContext db,
        Guid loanId,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        var query = db.LoanApplications.AsNoTracking().Where(l => l.Id == loanId);
        if (!caller.CanViewAllLoanBranches)
        {
            query = query.Where(l => l.BranchId == caller.BranchId);
        }

        return await query.AnyAsync(cancellationToken);
    }
}

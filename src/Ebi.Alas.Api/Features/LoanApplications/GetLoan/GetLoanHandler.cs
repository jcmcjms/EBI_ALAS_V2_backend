using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications.GetLoan;

public sealed class GetLoanHandler(AlasDbContext db)
{
    public async Task<LoanResponse> HandleAsync(Guid id, CallerContext caller, CancellationToken cancellationToken)
    {
        var query = db.LoanApplications.AsNoTracking().Where(l => l.Id == id);
        if (!caller.CanAccessAllBranches)
        {
            query = query.Where(l => l.BranchId == caller.BranchId);
        }

        var loan = await query.FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Loan", id.ToString());
        return LoanMapping.ToResponse(loan);
    }
}

public sealed class GetLoanByLamIdHandler(AlasDbContext db)
{
    public async Task<LoanResponse> HandleAsync(string lamId, CallerContext caller, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lamId);
        var query = db.LoanApplications.AsNoTracking().Where(l => l.LamId == lamId);
        if (!caller.CanAccessAllBranches)
        {
            query = query.Where(l => l.BranchId == caller.BranchId);
        }

        var loan = await query.FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Loan", lamId);
        return LoanMapping.ToResponse(loan);
    }
}

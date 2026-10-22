using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications.GetLoan;

public sealed class GetLoanHandler(AlasDbContext db)
{
    public async Task<LoanResponse> HandleAsync(Guid id, CallerContext caller, CancellationToken cancellationToken)
    {
        var loan = await LoanAccess.FindAccessibleAsync(db, id, caller, cancellationToken)
            ?? throw new Composition.Errors.NotFoundException("Loan", id.ToString());
        return await loan.ToResponseAsync(db, cancellationToken);
    }
}

public sealed class GetLoanByLamIdHandler(AlasDbContext db)
{
    public async Task<LoanResponse> HandleAsync(string lamId, CallerContext caller, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lamId);

        var query = db.LoanApplications.AsNoTracking().Where(l => l.LamId == lamId);
        if (!caller.CanViewAllLoanBranches)
        {
            // Resolve visibility from the loan's own id so authority/assignee rules apply.
            var id = await query.Select(l => l.Id).FirstOrDefaultAsync(cancellationToken);
            if (id == Guid.Empty)
            {
                throw new Composition.Errors.NotFoundException("Loan", lamId);
            }

            var loan = await LoanAccess.FindAccessibleAsync(db, id, caller, cancellationToken);
            return loan is null
                ? throw new Composition.Errors.NotFoundException("Loan", lamId)
                : await loan.ToResponseAsync(db, cancellationToken);
        }

        var found = await query.FirstOrDefaultAsync(cancellationToken)
            ?? throw new Composition.Errors.NotFoundException("Loan", lamId);
        return await found.ToResponseAsync(db, cancellationToken);
    }
}

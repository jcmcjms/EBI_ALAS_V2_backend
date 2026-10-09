using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanProducts;

public sealed class UpdateLoanProductPolicyHandler(AlasDbContext db)
{
    public async Task<LoanProductResponse> HandleAsync(
        string code,
        UpdateLoanProductPolicyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var product = await db.LoanProducts.FirstOrDefaultAsync(p => p.Code == code, cancellationToken)
            ?? throw new NotFoundException("Loan product", code);

        product.UpdatePolicy(
            request.MinAmount,
            request.MaxAmount,
            request.MinTermDays,
            request.MaxTermDays,
            request.NotarialFee,
            request.DocStampFee,
            request.InsuranceFee,
            request.AdvanceInterestRate);

        await db.SaveChangesAsync(cancellationToken);
        return product.ToResponse();
    }
}

using Ebi.Alas.Api.Features.LoanProducts;

namespace Ebi.Alas.Api.Features.LoanProducts;

public static class LoanProductMapping
{
    public static LoanProductResponse ToResponse(this LoanProduct product) => new(
        product.Code,
        product.Name,
        product.MinAmount,
        product.MaxAmount,
        product.MinTermDays,
        product.MaxTermDays,
        product.NotarialFee,
        product.DocStampFee,
        product.InsuranceFee,
        product.AdvanceInterestRate,
        product.ApplicationChargeRate,
        product.AmortizationMode,
        product.ChargeAdvanceInterest,
        IsRetired: !product.IsActive,
        product.LastSyncedAt);
}

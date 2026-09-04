using FluentValidation;

namespace EBI.ALAS.Api.Features.Loans;

public class UpdateLoanProductValidator : AbstractValidator<UpdateLoanProductRequest>
{
    public UpdateLoanProductValidator()
    {
        // Mirror of the service-side ValidatePolicyFields — both
        // layers defend, but the FluentValidation version emits a
        // structured ValidationFailed response (matches the rest of
        // the slice) while the service-side check throws on a
        // programmatic bypass.
        RuleFor(x => x.MinAmount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("MinAmount cannot be negative.");

        RuleFor(x => x.MaxAmount)
            .GreaterThanOrEqualTo(x => x.MinAmount)
            .WithMessage("MaxAmount must be greater than or equal to MinAmount.");

        RuleFor(x => x.MinTermMonths)
            .GreaterThanOrEqualTo(0)
            .WithMessage("MinTermMonths cannot be negative.");

        RuleFor(x => x.MaxTermMonths)
            .GreaterThanOrEqualTo(x => x.MinTermMonths)
            .WithMessage("MaxTermMonths must be greater than or equal to MinTermMonths.")
            .LessThanOrEqualTo(LoanProductService.AbsoluteMaxTermMonths)
            .WithMessage($"MaxTermMonths cannot exceed the absolute ceiling of {LoanProductService.AbsoluteMaxTermMonths} months (7 years).");

        RuleFor(x => x.NotarialFee)
            .GreaterThanOrEqualTo(0)
            .WithMessage("NotarialFee cannot be negative.");

        RuleFor(x => x.DocStampFee)
            .GreaterThanOrEqualTo(0)
            .WithMessage("DocStampFee cannot be negative.");

        RuleFor(x => x.InsuranceFee)
            .GreaterThanOrEqualTo(0)
            .WithMessage("InsuranceFee cannot be negative.");

        // AdvanceInterestRate stored as a fraction (0.12 = 12% p.a.).
        // 1.0 = 100% is the upper bound — anything higher is a data
        // error rather than a banking product.
        RuleFor(x => x.AdvanceInterestRate)
            .InclusiveBetween(0m, 1m)
            .WithMessage("AdvanceInterestRate must be between 0 and 1 (e.g. 0.12 for 12% p.a.).");
    }
}

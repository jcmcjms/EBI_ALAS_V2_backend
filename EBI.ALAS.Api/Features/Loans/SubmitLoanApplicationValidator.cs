using FluentValidation;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Server-side mirror of src/pages/loans/create/schema.ts. The Zod schema is
/// UX; this is the enforcement point. Bounds that depend on the loan-product
/// mirror (active flag, min/max amount, min/max term) are re-checked here per
/// loan because the client can tamper with any number in the payload.
/// </summary>
public class SubmitLoanApplicationValidator : AbstractValidator<SubmitLoanApplicationRequest>
{
    private const decimal FeeTolerance = 0.01m;
    private const int MinJustificationLength = 5;
    private static readonly int[] AllowedCreationTypes = [0, 1, 2, 6];

    public SubmitLoanApplicationValidator(ILoanProductRepository productRepository)
    {
        // ── §1.2 branch & type ──
        RuleFor(x => x.BranchType.Lai)
            .NotEmpty().WithMessage("LAI is required.")
            .Matches(@"^\d{3}-\d{2}-\d{4,6}-\d{1,2}$")
            .WithMessage("LAI must be in branch-account form, e.g. 011-05-13081-1.");
        RuleFor(x => x.BranchType.CreationTypeCode)
            .Must(c => c is null || AllowedCreationTypes.Contains(c.Value))
            .WithMessage("Unknown creation type code.");
        RuleFor(x => x.BranchType.CreationTypeLabel).MaximumLength(50);

        // ── §1.1 / §2 client snapshot (length caps = payload-bloat defense) ──
        RuleFor(x => x.Client.CisId).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Client.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Client.MiddleName).MaximumLength(100);
        RuleFor(x => x.Client.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Client.Suffix).MaximumLength(10);
        RuleFor(x => x.Client.Birthdate).Must(BeValidIsoDateOrNull).WithMessage("Birthdate must be an ISO date.");
        RuleFor(x => x.Client.Address).MaximumLength(500);
        RuleFor(x => x.Client.Agency).MaximumLength(100);
        RuleFor(x => x.Client.Position).MaximumLength(100);
        RuleFor(x => x.Client.EmployeeId).MaximumLength(50);
        RuleFor(x => x.Client.NetTakeHomePay).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Client.LengthOfService).MaximumLength(50);
        RuleFor(x => x.Client.Region).MaximumLength(10);
        RuleFor(x => x.Client.DivisionCode).MaximumLength(10);
        RuleFor(x => x.Client.StationCode).MaximumLength(10);
        RuleFor(x => x.Client.MisAgency).MaximumLength(200);
        RuleFor(x => x.Client.School).MaximumLength(200);
        RuleFor(x => x.Client.Referrer).MaximumLength(100);

        // ── §3 loans ──
        RuleFor(x => x.Loans).NotEmpty().WithMessage("Select at least one loan to process.")
            .Must(l => l.Count <= 10).WithMessage("An application cannot carry more than 10 loans.");
        RuleFor(x => x.Loans)
            .Must(l => l.Select(x => x.ProductCode).Distinct(StringComparer.Ordinal).Count() == l.Count)
            .WithMessage("Cannot select multiple loans of the same product.");
        RuleFor(x => x.Loans)
            .Must(l => l.Select(x => x.LoanNo).Distinct(StringComparer.Ordinal).Count() == l.Count)
            .WithMessage("Duplicate loan numbers in submission.");

        RuleForEach(x => x.Loans).SetValidator(new LoanSectionValidator(productRepository));

        // ── §4 / §5 obligations ──
        RuleForEach(x => x.OutstandingLoans).SetValidator(new OutstandingLoanSectionValidator());
        RuleForEach(x => x.EbiReloans).SetValidator(new EbiReloanSectionValidator());
        RuleForEach(x => x.BuyOuts).SetValidator(new BuyOutSectionValidator());
        RuleForEach(x => x.IncomingLoans).SetValidator(new IncomingLoanSectionValidator());

        // ── §6 verification ──
        RuleFor(x => x.Verification.Findings)
            .NotEmpty().WithMessage("Findings are required. Document what was verified.")
            .MaximumLength(2000);

        // ── §7 deviations (relational rules mirror the Zod superRefine) ──
        RuleFor(x => x.Deviations.OtherRemarks)
            .NotEmpty().WithMessage("Other remarks are required.");
        RuleFor(x => x.Deviations)
            .Must(d => !d.HasDeviations || d.DeviationDetails.Count > 0)
            .WithMessage("Select at least one deviation reason when the deviations flag is enabled.");
        RuleFor(x => x.Deviations)
            .Must(d => !d.HasDeviations || d.DeviationDetails.All(
                r => (d.DeviationJustifications.TryGetValue(r, out var j) ? j : "").Trim().Length >= MinJustificationLength))
            .WithMessage($"Every selected deviation reason needs a justification of at least {MinJustificationLength} characters.");
        RuleFor(x => x.Deviations.Remarks).MaximumLength(1000);
        RuleFor(x => x.Deviations.AoRecommendation).MaximumLength(1000);
        RuleFor(x => x.Deviations.FeeDeviationJustification).MaximumLength(1000);

        // Fee-override rule: any fee deviating from its policy snapshot
        // upgrades feeDeviationJustification to required.
        RuleFor(x => x)
            .Must((req, _) => !HasFeeOverride(req) ||
                              !string.IsNullOrWhiteSpace(req.Deviations.FeeDeviationJustification))
            .WithMessage("Provide a justification — at least one fee deviates from the bank's standard rate.")
            .WithName("Deviations.FeeDeviationJustification");
    }

    private static bool HasFeeOverride(SubmitLoanApplicationRequest req) =>
        req.Loans.Any(l =>
                Math.Abs(l.Parameters.NotarialFee - l.Parameters.StandardFeesSnapshot.NotarialFee) > FeeTolerance ||
                Math.Abs(l.Parameters.DocStamps - l.Parameters.StandardFeesSnapshot.DocStamps) > FeeTolerance ||
                Math.Abs(l.Parameters.Insurance - l.Parameters.StandardFeesSnapshot.Insurance) > FeeTolerance);

    private static bool BeValidIsoDateOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) || DateOnly.TryParseExact(value, "yyyy-MM-dd", out _);
}

public class LoanSectionValidator : AbstractValidator<LoanSection>
{
    public LoanSectionValidator(ILoanProductRepository productRepository)
    {
        RuleFor(x => x.LoanNo).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ProductCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.BranchCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.CreationTypeCode)
            .Must(c => c is null || new[] { 0, 1, 2, 6 }.Contains(c.Value))
            .WithMessage("Unknown creation type code.");

        RuleFor(x => x.Parameters.Product).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Parameters.Purpose).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Parameters.ProposedAmount)
            .GreaterThan(0).WithMessage("Proposed amount must be greater than 0")
            .LessThanOrEqualTo(5_000_000).WithMessage("Amount exceeds maximum allowed");
        RuleFor(x => x.Parameters.Term)
            .InclusiveBetween(1, LoanProductService.AbsoluteMaxTermDays)
            .WithMessage($"Term must be between 1 and {LoanProductService.AbsoluteMaxTermDays} days.");
        RuleFor(x => x.Parameters.InterestRate).InclusiveBetween(0, 100);
        RuleFor(x => x.Parameters.NthpDate)
            .Must(v => string.IsNullOrWhiteSpace(v) || DateOnly.TryParseExact(v, "yyyy-MM-dd", out _))
            .WithMessage("NTHP date must be an ISO date.");
        RuleFor(x => x.Parameters.NotarialFee).InclusiveBetween(0, 50_000);
        RuleFor(x => x.Parameters.DocStamps).InclusiveBetween(0, 50_000);
        RuleFor(x => x.Parameters.Insurance).InclusiveBetween(0, 50_000);

        // Product-mirror policy bounds, per loan (PK seeks; ≤10 loans per submit).
        RuleFor(x => x.ProductCode)
            .MustAsync(async (code, ct) => await productRepository.ExistsActiveByCodeAsync(code, ct))
            .WithMessage("Selected product is not currently offered.");
        RuleFor(x => x)
            .MustAsync(async (loan, ct) =>
            {
                var product = await productRepository.GetByCodeAsync(loan.ProductCode, ct);
                if (product is null) return true; // previous rule owns this case
                return loan.Parameters.ProposedAmount >= product.MinAmount
                    && loan.Parameters.ProposedAmount <= product.MaxAmount;
            })
            .WithMessage("Proposed amount must be within the product's allowed range.")
            .WithName("Parameters.ProposedAmount");
        RuleFor(x => x)
            .MustAsync(async (loan, ct) =>
            {
                var product = await productRepository.GetByCodeAsync(loan.ProductCode, ct);
                if (product is null) return true;
                return loan.Parameters.Term >= product.MinTermDays
                    && loan.Parameters.Term <= product.MaxTermDays;
            })
            .WithMessage("Term must be within the product's allowed range.")
            .WithName("Parameters.Term");
    }
}

public class OutstandingLoanSectionValidator : AbstractValidator<OutstandingLoanSection>
{
    public OutstandingLoanSectionValidator()
    {
        RuleFor(x => x.Pn).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Status).MaximumLength(100);
        RuleFor(x => x.ProductWithDescription).MaximumLength(200);
        RuleFor(x => x.PrincipalBalance).GreaterThanOrEqualTo(0);
        RuleFor(x => x.OutstandingBalance).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DateGranted).Must(v => string.IsNullOrWhiteSpace(v) || DateOnly.TryParseExact(v, "yyyy-MM-dd", out _));
        RuleFor(x => x.DateMaturity).Must(v => string.IsNullOrWhiteSpace(v) || DateOnly.TryParseExact(v, "yyyy-MM-dd", out _));
    }
}

public class EbiReloanSectionValidator : AbstractValidator<EbiReloanSection>
{
    public EbiReloanSectionValidator()
    {
        RuleFor(x => x.Pn).MaximumLength(50);
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.ExistingDeduction).GreaterThanOrEqualTo(0);
        RuleFor(x => x.OutstandingBalance).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PayToClose)
            .GreaterThanOrEqualTo(0);
    }
}

public class BuyOutSectionValidator : AbstractValidator<BuyOutSection>
{
    public BuyOutSectionValidator()
    {
        RuleFor(x => x.Pn).MaximumLength(50);
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.Amortization).GreaterThanOrEqualTo(0);
        RuleFor(x => x.OutstandingBalance).GreaterThanOrEqualTo(0);
    }
}

public class IncomingLoanSectionValidator : AbstractValidator<IncomingLoanSection>
{
    public IncomingLoanSectionValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.Deductions).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Remarks).MaximumLength(500);
    }
}
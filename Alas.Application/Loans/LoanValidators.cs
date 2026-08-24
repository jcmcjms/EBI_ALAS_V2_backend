using Alas.Application.Loans;
using FluentValidation;

namespace Alas.Application.Loans;

public sealed class CreateLoanRequestValidator : AbstractValidator<CreateLoanRequest>
{
    public CreateLoanRequestValidator()
    {
        RuleFor(x => x.BorrowerName)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.BorrowerContact)
            .MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.BorrowerContact));

        RuleFor(x => x.PrincipalAmount)
            .GreaterThan(0)
            .LessThan(100_000_000m);

        RuleFor(x => x.InterestRate)
            .InclusiveBetween(0.01m, 100m);

        RuleFor(x => x.TermMonths)
            .InclusiveBetween(1, 360);

        RuleFor(x => x.Purpose)
            .MaximumLength(1000)
            .When(x => !string.IsNullOrWhiteSpace(x.Purpose));

        RuleFor(x => x.BranchId)
            .MaximumLength(100)
            .When(x => !string.IsNullOrWhiteSpace(x.BranchId));

        RuleFor(x => x.Remarks)
            .MaximumLength(2000)
            .When(x => !string.IsNullOrWhiteSpace(x.Remarks));
    }
}

public sealed class ApproveLoanRequestValidator : AbstractValidator<ApproveLoanRequest>
{
    public ApproveLoanRequestValidator()
    {
        RuleFor(x => x.Remarks)
            .MaximumLength(2000)
            .When(x => !string.IsNullOrWhiteSpace(x.Remarks));
    }
}

public sealed class RejectLoanRequestValidator : AbstractValidator<RejectLoanRequest>
{
    public RejectLoanRequestValidator()
    {
        RuleFor(x => x.RejectionReason)
            .NotEmpty()
            .MaximumLength(2000);
    }
}

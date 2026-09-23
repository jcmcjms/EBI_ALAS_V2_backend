using EBI.ALAS.Api.Features.Loans;
using FluentValidation;
using Xunit;

namespace EBI.ALAS.Tests;

public class SubmitLoanApplicationValidatorTests
{
    private sealed class StubProducts : ILoanProductRepository
    {
        public Task<bool> ExistsActiveByCodeAsync(string code, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<LoanProduct?> GetByCodeAsync(string code, CancellationToken ct = default) =>
            Task.FromResult<LoanProduct?>(new LoanProduct
            {
                Code = code,
                MinAmount = 1,
                MaxAmount = 10_000_000,
                MinTermDays = 1,
                MaxTermDays = 5000,
            });

        public Task<IReadOnlyList<LoanProduct>> GetAllAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<LoanProduct> UpsertAsync(LoanProduct product, bool preservePolicyFields, int? updatedByUserId, DateTime updatedDate, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<bool> DeleteAsync(string code, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private static LoanSection ValidLoan(string loanNo, string product) => new()
    {
        LoanNo = loanNo,
        ProductCode = product,
        BranchCode = "011",
        CreationTypeCode = 0,
        CreationTypeLabel = "New Loan",
        Parameters = new LoanParametersSection
        {
            Product = product,
            Purpose = "Test purpose",
            ProposedAmount = 100_000,
            Term = 720,
            StandardFeesSnapshot = new FeeSnapshotSection(),
        },
        Verification = new VerificationSection { Findings = "Employment confirmed." },
        Deviations = new DeviationsSection { OtherRemarks = "None." },
    };

    private static SubmitLoanApplicationRequest ValidRequest(params LoanSection[] loans) => new()
    {
        BranchType = new BranchTypeSection
        {
            Lai = "011-05-13081-1",
            CreationTypeLabel = "New Loan",
        },
        Client = new ClientSection
        {
            CisId = "1",
            FirstName = "Juan",
            LastName = "Dela Cruz",
        },
        Loans = loans,
    };

    [Fact]
    public async Task Missing_findings_on_second_loan_fails_only_that_loan()
    {
        var request = ValidRequest(
            ValidLoan("CL1", "C21"),
            ValidLoan("CL2", "C02") with
            {
                Verification = new VerificationSection { Findings = "" },
            });

        var result = await new SubmitLoanApplicationValidator(new StubProducts())
            .ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Loans[1].Verification.Findings");
        Assert.DoesNotContain(result.Errors, e => e.PropertyName == "Loans[0].Verification.Findings");
    }

    [Fact]
    public async Task Fee_override_requires_that_loans_own_justification()
    {
        var loan = ValidLoan("CL1", "C21");
        loan = loan with
        {
            Parameters = loan.Parameters with { NotarialFee = 900m },
        };

        var request = ValidRequest(loan);
        var validator = new SubmitLoanApplicationValidator(new StubProducts());

        var result = await validator.ValidateAsync(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.PropertyName == "Loans[0].Deviations.FeeDeviationJustification");

        var justified = loan with
        {
            Deviations = loan.Deviations with
            {
                FeeDeviationJustification = "Notary charged per page.",
            },
        };
        var ok = await validator.ValidateAsync(ValidRequest(justified));
        Assert.True(ok.IsValid);
    }

    [Fact]
    public async Task Multiple_loans_each_need_their_own_justification()
    {
        var loan1 = ValidLoan("CL1", "C21") with
        {
            Parameters = ValidLoan("CL1", "C21").Parameters with { NotarialFee = 900m },
            Deviations = new DeviationsSection
            {
                OtherRemarks = "None.",
                FeeDeviationJustification = "Notary charged per page.",
            },
        };

        var loan2 = ValidLoan("CL2", "C02") with
        {
            Parameters = ValidLoan("CL2", "C02").Parameters with { DocStamps = 500m },
            Deviations = new DeviationsSection { OtherRemarks = "None." },
        };

        var result = await new SubmitLoanApplicationValidator(new StubProducts())
            .ValidateAsync(ValidRequest(loan1, loan2));

        Assert.False(result.IsValid);
        Assert.DoesNotContain(result.Errors, e =>
            e.PropertyName == "Loans[0].Deviations.FeeDeviationJustification");
        Assert.Contains(result.Errors, e =>
            e.PropertyName == "Loans[1].Deviations.FeeDeviationJustification");
    }

    [Fact]
    public async Task Deviations_requires_justification_per_selected_reason()
    {
        var loan = ValidLoan("CL1", "C21") with
        {
            Deviations = new DeviationsSection
            {
                HasDeviations = true,
                DeviationDetails = ["Age not within the prescribed parameters"],
                DeviationJustifications = new Dictionary<string, string>(),
                OtherRemarks = "None.",
            },
        };

        var result = await new SubmitLoanApplicationValidator(new StubProducts())
            .ValidateAsync(ValidRequest(loan));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.PropertyName == "Loans[0].Deviations");
    }

    [Fact]
    public async Task Valid_multi_loan_submission_passes()
    {
        var request = ValidRequest(
            ValidLoan("CL1", "C21"),
            ValidLoan("CL2", "C02"));

        var result = await new SubmitLoanApplicationValidator(new StubProducts())
            .ValidateAsync(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Root_level_outstanding_loans_still_validated()
    {
        var request = ValidRequest(ValidLoan("CL1", "C21"));
        request = request with
        {
            OutstandingLoans =
            [
                new OutstandingLoanSection { Pn = "PN001", PrincipalBalance = 50_000 },
            ],
        };

        var result = await new SubmitLoanApplicationValidator(new StubProducts())
            .ValidateAsync(request);

        Assert.True(result.IsValid);
    }
}

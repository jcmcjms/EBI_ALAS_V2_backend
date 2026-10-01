using EBI.ALAS.Api.Features.Loans;
using NSubstitute;
using Xunit;
namespace EBI.ALAS.Tests;
public class DeviationRemarkValidationTests
{
    private const string AgeReason = "Age not within the prescribed parameters";
    private static SubmitLoanApplicationValidator Validator() =>
        new(Substitute.For<ILoanProductRepository>());
    private static SubmitLoanApplicationRequest RequestWith(DeviationsSection deviations) => new()
    {
        BranchType = new BranchTypeSection
        {
            CreationTypeCode = 0,
            CreationTypeLabel = "New Loan",
            Branch = "011",
            RequestingOfficer = "JOAN DOE",
            Lai = "011-05-13081-1",
        },
        Client = new ClientSection { CisId = "CIS-1", FirstName = "JUAN", LastName = "DELA CRUZ" },
        Loans =
        [
            new LoanSection
            {
                LoanNo = "PN-1",
                ProductCode = "C21",
                ProductDescription = "C21 - Salary Loan",
                CreationTypeCode = 0,
                CreationTypeLabel = "New Loan",
                BranchCode = "011",
                Parameters = new LoanParametersSection
                {
                    Product = "C21 - Salary Loan",
                    Purpose = "Consumption",
                    ProposedAmount = 50_000m,
                    Term = 360,
                    InterestRate = 21m,
                    StandardFeesSnapshot = new FeeSnapshotSection(),
                },
                Verification = new VerificationSection { Findings = "Verified employment and income." },
                Deviations = deviations,
            },
        ],
    };
    private static async Task<List<string>> FailureMessagesAsync(DeviationsSection deviations)
    {
        var result = await Validator().ValidateAsync(RequestWith(deviations));
        return result.Errors.Select(e => e.ErrorMessage).ToList();
    }
    [Fact]
    public async Task Remark_over_1000_chars_is_rejected()
    {
        var messages = await FailureMessagesAsync(new DeviationsSection
        {
            HasDeviations = true,
            DeviationDetails = [AgeReason],
            DeviationJustifications = new Dictionary<string, string> { [AgeReason] = new string('A', 1001) },
            OtherRemarks = "ok",
        });
        Assert.Contains("Each deviation remark must be 1000 characters or fewer.", messages);
    }
    [Fact]
    public async Task Remark_at_1000_chars_passes_the_length_rule()
    {
        var messages = await FailureMessagesAsync(new DeviationsSection
        {
            HasDeviations = true,
            DeviationDetails = [AgeReason],
            DeviationJustifications = new Dictionary<string, string> { [AgeReason] = new string('A', 1000) },
            OtherRemarks = "ok",
        });
        Assert.DoesNotContain("Each deviation remark must be 1000 characters or fewer.", messages);
    }
    [Fact]
    public async Task Duplicate_deviation_reasons_are_rejected()
    {
        var messages = await FailureMessagesAsync(new DeviationsSection
        {
            HasDeviations = true,
            DeviationDetails = [AgeReason, AgeReason],
            DeviationJustifications = new Dictionary<string, string> { [AgeReason] = "Strong co-maker and collateral." },
            OtherRemarks = "ok",
        });
        Assert.Contains("Duplicate deviation reasons in submission.", messages);
    }
    [Fact]
    public async Task Remark_below_minimum_length_is_still_rejected()
    {
        var messages = await FailureMessagesAsync(new DeviationsSection
        {
            HasDeviations = true,
            DeviationDetails = [AgeReason],
            DeviationJustifications = new Dictionary<string, string> { [AgeReason] = "ok" },
            OtherRemarks = "ok",
        });
        Assert.Contains("Every selected deviation reason needs a justification of at least 5 characters.", messages);
    }
}

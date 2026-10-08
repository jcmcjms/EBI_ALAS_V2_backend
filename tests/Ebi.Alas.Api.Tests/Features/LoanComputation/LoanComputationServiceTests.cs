using Ebi.Alas.Api.Features.LoanComputation;

namespace Ebi.Alas.Api.Tests.Features.LoanComputation;

public sealed class LoanComputationServiceTests
{
    private readonly LoanComputationService _service = new();

    [Fact]
    public void Compute_SimpleInterestOverTwoMonths()
    {
        var result = _service.Compute(new LoanComputationInput(
            Principal: 10000m,
            TermDays: 60,
            InterestRatePerMonth: 2m));

        Assert.Equal(400m, result.TotalInterest);
        Assert.Equal(0m, result.TotalDeductions);
        Assert.Equal(10000m, result.NetProceeds);
        Assert.Equal(1155.56m, result.Amortization);
    }

    [Fact]
    public void Compute_DeductionsReduceNetProceeds()
    {
        var result = _service.Compute(new LoanComputationInput(
            Principal: 10000m,
            TermDays: 60,
            InterestRatePerMonth: 2m,
            ServiceFee: 100m,
            InsuranceFee: 50m,
            OtherDeductions: 25m));

        Assert.Equal(175m, result.TotalDeductions);
        Assert.Equal(9825m, result.NetProceeds);
    }

    [Fact]
    public void Compute_Mic_UsesHalfInterest()
    {
        var result = _service.Compute(new LoanComputationInput(
            Principal: 10000m,
            TermDays: 60,
            InterestRatePerMonth: 2m,
            AmortizationType: AmortizationType.Mic));

        Assert.Equal(200m, result.TotalInterest);
    }

    [Fact]
    public void Compute_AdoLump_PaysInOneInstallment()
    {
        var result = _service.Compute(new LoanComputationInput(
            Principal: 10000m,
            TermDays: 60,
            InterestRatePerMonth: 2m,
            AmortizationType: AmortizationType.AdoLump));

        Assert.Equal(10400m, result.Amortization);
    }

    [Fact]
    public void Compute_NthpAndMaxLoanable()
    {
        var result = _service.Compute(new LoanComputationInput(
            Principal: 10000m,
            TermDays: 60,
            InterestRatePerMonth: 2m,
            GrossMonthlyIncome: 20000m,
            MonthlyLivingExpense: 5000m,
            ExistingMonthlyAmortization: 1000m));

        Assert.Equal(15000m, result.Nthp);
        Assert.True(result.MaxLoanable > 0);
        Assert.True(result.PassesNthpCheck || !result.PassesNthpCheck);
    }

    [Fact]
    public void Compute_RejectsNonPositivePrincipal()
    {
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
            _service.Compute(new LoanComputationInput(0m, 30, 1m)));
    }
}

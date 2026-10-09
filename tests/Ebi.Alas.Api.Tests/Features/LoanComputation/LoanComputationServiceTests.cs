using Ebi.Alas.Api.Features.LoanComputation;

namespace Ebi.Alas.Api.Tests.Features.LoanComputation;

public sealed class LoanComputationServiceTests
{
    private readonly LoanComputationService _service = new();

    [Fact]
    public void ComputeMonthlyAmortization_MatchesAnnuityPmt()
    {
        // 100_000 @ 12% p.a. / 12 months ≈ 8_884.88 → ceil
        var amort = LoanComputationService.ComputeMonthlyAmortization(100_000m, 12m, 360);
        Assert.Equal(8885m, amort);
    }

    [Fact]
    public void Compute_ZeroRate_SplitsPrincipalEvenly()
    {
        var amort = LoanComputationService.ComputeMonthlyAmortization(12_000m, 0m, 360);
        Assert.Equal(1000m, amort);
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
    public void Compute_AmortizationTimesMonthsCoversPrincipalPlusInterest()
    {
        var result = _service.Compute(new LoanComputationInput(
            Principal: 10000m,
            TermDays: 60,
            InterestRatePerMonth: 12m));

        Assert.True(result.Amortization > 0);
        Assert.Equal(2m * result.Amortization, 10000m + result.TotalInterest);
    }

    [Fact]
    public void Compute_NthpAndMaxLoanable()
    {
        var result = _service.Compute(new LoanComputationInput(
            Principal: 10000m,
            TermDays: 360,
            InterestRatePerMonth: 12m,
            GrossMonthlyIncome: 20000m,
            MonthlyLivingExpense: 5000m,
            ExistingMonthlyAmortization: 1000m));

        Assert.Equal(15000m, result.Nthp);
        Assert.True(result.MaxLoanable >= 0);
    }

    [Fact]
    public void Compute_RejectsNonPositivePrincipal()
    {
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
            _service.Compute(new LoanComputationInput(0m, 30, 1m)));
    }
}

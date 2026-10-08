namespace Ebi.Alas.Api.Features.LoanComputation;

public enum AmortizationType
{
    Diminishing = 1,
    Mic = 2,
    AdoLump = 3
}

public sealed record LoanComputationInput(
    decimal Principal,
    int TermDays,
    decimal InterestRatePerMonth,
    AmortizationType AmortizationType = AmortizationType.Diminishing,
    decimal ServiceFee = 0,
    decimal InsuranceFee = 0,
    decimal OtherDeductions = 0,
    decimal GrossMonthlyIncome = 0,
    decimal ExistingMonthlyAmortization = 0,
    decimal MonthlyLivingExpense = 0);

public sealed record LoanComputationResult(
    decimal Principal,
    decimal TotalInterest,
    decimal TotalDeductions,
    decimal NetProceeds,
    decimal Amortization,
    decimal Nthp,
    decimal DisposableIncome,
    decimal MaxLoanable,
    bool PassesNthpCheck,
    bool PassesDisposableIncomeCheck);

public sealed class LoanComputationService
{
    public const decimal NthpThresholdRatio = 0.40m;

    public LoanComputationResult Compute(LoanComputationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfLessThan(input.Principal, 0.01m);
        ArgumentOutOfRangeException.ThrowIfLessThan(input.TermDays, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(input.InterestRatePerMonth);

        var months = input.TermDays / 30.0m;
        var totalInterest = ComputeInterest(input.Principal, input.InterestRatePerMonth, months, input.AmortizationType);
        var totalDeductions = input.ServiceFee + input.InsuranceFee + input.OtherDeductions;
        var netProceeds = input.Principal - totalDeductions;
        var totalPayable = input.Principal + totalInterest;
        var periods = Math.Max(1, (int)Math.Ceiling(input.TermDays / 7.0m));
        var amortization = input.AmortizationType == AmortizationType.AdoLump
            ? totalPayable
            : decimal.Round(totalPayable / periods, 2, MidpointRounding.AwayFromZero);

        var nthp = ComputeNthp(input.GrossMonthlyIncome, input.MonthlyLivingExpense);
        var monthlyAmortization = decimal.Round(amortization * 4.3333m, 2, MidpointRounding.AwayFromZero);
        var disposable = nthp - input.ExistingMonthlyAmortization - monthlyAmortization;
        var maxLoanable = ComputeMaxLoanable(nthp, input.InterestRatePerMonth, months, input.AmortizationType);

        return new LoanComputationResult(
            input.Principal,
            totalInterest,
            totalDeductions,
            netProceeds,
            amortization,
            nthp,
            disposable,
            maxLoanable,
            nthp > 0 && monthlyAmortization <= nthp * NthpThresholdRatio,
            disposable >= 0);
    }

    private static decimal ComputeInterest(
        decimal principal,
        decimal ratePerMonth,
        decimal months,
        AmortizationType type) => type switch
    {
        AmortizationType.Diminishing => decimal.Round(
            principal * (ratePerMonth / 100m) * months,
            2,
            MidpointRounding.AwayFromZero),
        AmortizationType.Mic => decimal.Round(
            principal * (ratePerMonth / 100m) * months * 0.5m,
            2,
            MidpointRounding.AwayFromZero),
        AmortizationType.AdoLump => decimal.Round(
            principal * (ratePerMonth / 100m) * months,
            2,
            MidpointRounding.AwayFromZero),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static decimal ComputeNthp(decimal grossMonthlyIncome, decimal livingExpense) =>
        grossMonthlyIncome <= 0
            ? 0
            : Math.Max(0, grossMonthlyIncome - livingExpense);

    private static decimal ComputeMaxLoanable(
        decimal nthp,
        decimal ratePerMonth,
        decimal months,
        AmortizationType type)
    {
        if (nthp <= 0 || months <= 0)
        {
            return 0;
        }

        var allowedMonthly = decimal.Round(nthp * NthpThresholdRatio, 2, MidpointRounding.AwayFromZero);
        var periods = Math.Max(1, months * 4.3333m);
        var factor = 1 + (ratePerMonth / 100m) * months;
        if (type == AmortizationType.Mic)
        {
            factor = 1 + (ratePerMonth / 100m) * months * 0.5m;
        }

        return decimal.Round((allowedMonthly * periods) / factor, 2, MidpointRounding.AwayFromZero);
    }
}

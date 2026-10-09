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

/// <summary>
/// Monthly amortization and capacity math shared with the approval form
/// (annuity PMT over termMonths = floor(termDays / 30)).
/// </summary>
public sealed class LoanComputationService
{
    public const decimal NthpThresholdRatio = 0.40m;
    public const int DaysPerMonth = 30;

    public LoanComputationResult Compute(LoanComputationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfLessThan(input.Principal, 0.01m);
        ArgumentOutOfRangeException.ThrowIfLessThan(input.TermDays, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(input.InterestRatePerMonth);

        var termMonths = Math.Max(1, input.TermDays / DaysPerMonth);
        var monthlyAmortization = ComputeMonthlyAmortization(
            input.Principal,
            input.InterestRatePerMonth,
            input.TermDays,
            input.AmortizationType);

        var totalPayable = decimal.Round(monthlyAmortization * termMonths, 2, MidpointRounding.AwayFromZero);
        var totalInterest = Math.Max(0, totalPayable - input.Principal);
        var totalDeductions = input.ServiceFee + input.InsuranceFee + input.OtherDeductions;
        var netProceeds = input.Principal - totalDeductions;

        var nthp = ComputeNthp(input.GrossMonthlyIncome, input.MonthlyLivingExpense);
        var disposable = nthp - input.ExistingMonthlyAmortization - monthlyAmortization;
        var maxLoanable = ComputeMaxLoanable(
            nthp - input.ExistingMonthlyAmortization,
            input.InterestRatePerMonth,
            input.TermDays,
            input.AmortizationType);

        return new LoanComputationResult(
            input.Principal,
            totalInterest,
            totalDeductions,
            netProceeds,
            monthlyAmortization,
            nthp,
            disposable,
            maxLoanable,
            nthp > 0 && monthlyAmortization <= nthp * NthpThresholdRatio,
            disposable >= 0);
    }

    public static decimal ComputeMonthlyAmortization(
        decimal principal,
        decimal annualRatePercent,
        int termDays,
        AmortizationType type = AmortizationType.Diminishing)
    {
        if (principal <= 0 || termDays <= 0)
        {
            return 0;
        }

        var termMonths = termDays / DaysPerMonth;
        if (termMonths <= 0)
        {
            return 0;
        }

        if (type == AmortizationType.AdoLump)
        {
            return decimal.Round(principal * (1 + (annualRatePercent / 100m) * (termDays / 360m)), 2, MidpointRounding.AwayFromZero);
        }

        if (annualRatePercent == 0)
        {
            return decimal.Round(principal / termMonths, 2, MidpointRounding.AwayFromZero);
        }

        // Same annuity as the approval form: pmt = P·r / (1 − (1+r)^−n), rounded up.
        var r = Math.Round(annualRatePercent / 100m / 12m, 6, MidpointRounding.AwayFromZero);
        var n = termMonths;
        var pmt = (principal * r) / (1m - (decimal)Math.Pow((double)(1 + r), -n));
        return decimal.Ceiling(pmt);
    }

    private static decimal ComputeNthp(decimal grossMonthlyIncome, decimal livingExpense) =>
        grossMonthlyIncome <= 0
            ? 0
            : Math.Max(0, grossMonthlyIncome - livingExpense);

    private static decimal ComputeMaxLoanable(
        decimal monthlyCapacity,
        decimal annualRatePercent,
        int termDays,
        AmortizationType type)
    {
        if (monthlyCapacity == 0 || termDays <= 0)
        {
            return 0;
        }

        var termMonths = termDays / DaysPerMonth;
        if (termMonths <= 0)
        {
            return 0;
        }

        if (type == AmortizationType.AdoLump)
        {
            var factor = 1 + (annualRatePercent / 100m) * (termDays / 360m);
            return decimal.Round(Math.Abs(monthlyCapacity) / factor, 2, MidpointRounding.AwayFromZero);
        }

        var r = Math.Round(annualRatePercent / 100m / 12m, 6, MidpointRounding.AwayFromZero);
        var abs = Math.Abs(monthlyCapacity);
        var pv = r == 0
            ? abs * termMonths
            : (abs * (1m - (decimal)Math.Pow((double)(1 + r), -termMonths))) / r;

        var floored = decimal.Floor(pv / 100m) * 100m;
        return Math.Sign(monthlyCapacity) * floored;
    }
}

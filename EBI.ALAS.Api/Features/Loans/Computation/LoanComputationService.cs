namespace EBI.ALAS.Api.Features.Loans.Computation;
public record FeeSchedule(
    decimal ApplicationChargeRate,
    decimal DocStampRate,
    decimal NotarialFee,
    decimal InsuranceRate);
public record DeductionPolicy(
    DeductionPolicyMode Mode,
    decimal TotalRate);
public enum DeductionPolicyMode
{
    FixedTotalRate,
    SumOfComponents
}
public record LoanProductComputationConfig(
    string ProductCode,
    decimal AnnualInterestRate,
    int TermDays,
    AmortizationMode AmortizationMode,
    bool ChargeAdvanceInterest,
    FeeSchedule Fees,
    DeductionPolicy DeductionPolicy,
    int PolicyTermMonths = 0,
    decimal MaxLoanableStep = 100m,
    IReadOnlyList<MinimumAmortizationTier>? MinimumAmortizationTiers = null)
{
    public static LoanProductComputationConfig FromEntity(
        LoanProduct product,
        decimal interestRate,
        int termDays)
    {
        var isDim = (product.AmortizationMode ?? "DIM") == "DIM";
        var deductionPolicy = isDim
            ? new DeductionPolicy(DeductionPolicyMode.FixedTotalRate, 0.06m)
            : new DeductionPolicy(DeductionPolicyMode.SumOfComponents, 0m);
        return new LoanProductComputationConfig(
            ProductCode: product.Code,
            AnnualInterestRate: interestRate,
            TermDays: termDays,
            AmortizationMode: product.AmortizationMode == "MIC"
                ? Computation.AmortizationMode.MIC
                : Computation.AmortizationMode.DIM,
            ChargeAdvanceInterest: product.ChargeAdvanceInterest,
            Fees: new FeeSchedule(
                ApplicationChargeRate: product.ApplicationChargeRate,
                DocStampRate: product.DocStampFee > 0 ? product.DocStampFee : 0m,
                NotarialFee: product.NotarialFee,
                InsuranceRate: product.InsuranceFee > 0 ? product.InsuranceFee : 0m),
            DeductionPolicy: deductionPolicy,
            PolicyTermMonths: termDays > 0 ? (int)Math.Round(termDays / 30m) : 0,
            MaxLoanableStep: 100m);
    }
}
public enum AmortizationMode { DIM, MIC }
public record MinimumAmortizationTier(decimal From, decimal To, decimal MinimumAmortization);
public record LoanFees(
    decimal ApplicationCharge,
    decimal DocStamp,
    decimal NotarialFee,
    decimal Insurance,
    decimal AdvanceInterest);
public record ObligationRow(decimal Deductions, decimal OutstandingBalance);
public record LoanComputationInput(
    decimal ProposedAmount,
    LoanProductComputationConfig Product,
    LoanFees Fees,
    decimal NetTakeHomePay,
    decimal MinimumNthp,
    IReadOnlyList<decimal> OutstandingPrincipalBalances,
    IReadOnlyList<ObligationRow> Reloans,
    IReadOnlyList<ObligationRow> BuyOuts,
    IReadOnlyList<decimal> IncomingDeductions);
public record LoanComputationResults(
    LoanFees Fees,
    decimal TotalDeductions,
    decimal DeductionRate,
    decimal GrossProceeds,
    decimal TotalAccountsBalance,
    decimal NetProceedsOnDS,
    decimal TotalBuyOutBalance,
    decimal NetProceedsToClient,
    decimal TermMonths,
    decimal MonthlyAmortization,
    decimal TotalExposure,
    decimal NetPayAfterDeduction,
    decimal GrossDisposableIncome,
    decimal CapacityDeductions,
    decimal NetDisposableIncome,
    decimal MaximumLoanableAmount,
    bool AmortizationExceedsDisposable,
    bool NthpBelowMinimum);
public interface ILoanComputationService
{
    LoanFees ComputeExpectedFees(LoanProductComputationConfig product, decimal proposedAmount);
    LoanComputationResults ComputeLoanMetrics(LoanComputationInput input);
}
public sealed class LoanComputationService : ILoanComputationService
{
    private static readonly IReadOnlyDictionary<string, decimal> AtmHardCaps =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["C34"] = 200_000m,
            ["C21"] = 135_000m,
            ["C27"] = 120_000m,
            ["C29"] = 100_000m,
            ["C25"] = 200_000m,
        };
    public LoanFees ComputeExpectedFees(LoanProductComputationConfig product, decimal proposedAmount)
    {
        var docStamp = Round(proposedAmount * product.Fees.DocStampRate);
        var notarial = product.Fees.NotarialFee;
        var insurance = Round(proposedAmount * product.Fees.InsuranceRate);
        var advanceInterest = product.ChargeAdvanceInterest
            ? Round(proposedAmount * product.AnnualInterestRate * product.TermDays / 360m)
            : 0m;
        decimal applicationCharge;
        if (product.DeductionPolicy?.Mode == DeductionPolicyMode.FixedTotalRate)
        {
            var totalDeductions = Round(proposedAmount * product.DeductionPolicy.TotalRate);
            applicationCharge = Math.Max(0m, Round(totalDeductions - docStamp - notarial - insurance));
        }
        else
        {
            applicationCharge = Round(proposedAmount * product.Fees.ApplicationChargeRate);
        }
        return new LoanFees(applicationCharge, docStamp, notarial, insurance, advanceInterest);
    }
    public LoanComputationResults ComputeLoanMetrics(LoanComputationInput input)
    {
        var proposed = input.ProposedAmount;
        var product = input.Product;
        var totalDeductions = Round(
            input.Fees.ApplicationCharge
            + input.Fees.DocStamp
            + input.Fees.NotarialFee
            + input.Fees.Insurance
            + input.Fees.AdvanceInterest);
        var grossProceeds = Round(proposed - totalDeductions);
        var totalAccountsBalance = Round(Sum(input.Reloans.Select(r => r.OutstandingBalance)));
        var netProceedsOnDS = Round(grossProceeds - totalAccountsBalance);
        var totalBuyOutBalance = Round(Sum(input.BuyOuts.Select(b => b.OutstandingBalance)));
        var netProceedsToClient = Round(netProceedsOnDS - totalBuyOutBalance);
        var termMonths = product.PolicyTermMonths > 0
            ? product.PolicyTermMonths
            : product.TermDays / 30m;
        var factor = AnnuityFactor(product.AnnualInterestRate / 12m, termMonths);
        var diminishingAmortization = Round(proposed * factor);
        var minimumAmortization = product.AmortizationMode == AmortizationMode.MIC
            ? product.MinimumAmortizationTiers?
                  .FirstOrDefault(t => proposed >= t.From && proposed <= t.To)?.MinimumAmortization ?? 0m
            : 0m;
        var monthlyAmortization = Math.Max(diminishingAmortization, minimumAmortization);
        var totalExposure = Round(proposed + Sum(input.OutstandingPrincipalBalances));
        var releasedDeductions = Round(
            Sum(input.Reloans.Select(r => r.Deductions))
            + Sum(input.BuyOuts.Select(b => b.Deductions)));
        var netPayAfterDeduction = Round(input.NetTakeHomePay - monthlyAmortization + releasedDeductions);
        var grossDisposableIncome = Round(input.NetTakeHomePay + releasedDeductions);
        var capacityDeductions = Round(input.MinimumNthp + Sum(input.IncomingDeductions));
        var netDisposableIncome = Round(grossDisposableIncome - capacityDeductions);
        decimal maximumLoanableAmount;
        if (AtmHardCaps.TryGetValue(product.ProductCode, out var hardCap))
        {
            maximumLoanableAmount = hardCap;
        }
        else
        {
            maximumLoanableAmount = factor > 0
                ? FloorToStep(netDisposableIncome / factor, product.MaxLoanableStep)
                : 0m;
        }
        var amortizationExceedsDisposable = monthlyAmortization > netDisposableIncome;
        var nthpBelowMinimum = input.NetTakeHomePay < input.MinimumNthp;
        return new LoanComputationResults(
            Fees: input.Fees,
            TotalDeductions: totalDeductions,
            DeductionRate: proposed > 0 ? totalDeductions / proposed : 0m,
            GrossProceeds: grossProceeds,
            TotalAccountsBalance: totalAccountsBalance,
            NetProceedsOnDS: netProceedsOnDS,
            TotalBuyOutBalance: totalBuyOutBalance,
            NetProceedsToClient: netProceedsToClient,
            TermMonths: termMonths,
            MonthlyAmortization: monthlyAmortization,
            TotalExposure: totalExposure,
            NetPayAfterDeduction: netPayAfterDeduction,
            GrossDisposableIncome: grossDisposableIncome,
            CapacityDeductions: capacityDeductions,
            NetDisposableIncome: netDisposableIncome,
            MaximumLoanableAmount: maximumLoanableAmount,
            AmortizationExceedsDisposable: amortizationExceedsDisposable,
            NthpBelowMinimum: nthpBelowMinimum);
    }
    public static decimal AnnuityFactor(decimal monthlyRate, decimal termMonths)
    {
        if (termMonths <= 0) return 0m;
        monthlyRate = Math.Round(monthlyRate, 6, MidpointRounding.AwayFromZero);
        if (monthlyRate == 0m) return 1m / termMonths;
        var pow = DecimalPow(1m + monthlyRate, (int)termMonths);
        return monthlyRate * pow / (pow - 1m);
    }
    private static decimal DecimalPow(decimal baseValue, int exponent)
    {
        if (exponent < 0)
            throw new ArgumentOutOfRangeException(nameof(exponent), "Exponent must be non-negative.");
        decimal result = 1m;
        decimal factor = baseValue;
        for (var n = exponent; n > 0; n >>= 1)
        {
            if ((n & 1) == 1)
                result *= factor;
            factor *= factor;
        }
        return result;
    }
    private static decimal Sum(IEnumerable<decimal> values) => values.Sum();
    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal FloorToStep(decimal value, decimal step) =>
        step > 0m ? Math.Floor(value / step) * step : value;
}

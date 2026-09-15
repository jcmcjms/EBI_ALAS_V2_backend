namespace EBI.ALAS.Api.Features.Loans.Computation;

// ─── Config & input records ─────────────────────────────────────────────

/// <summary>Per-product fee schedule rates. Config-driven from LoanProduct table.</summary>
public record FeeSchedule(
    decimal ApplicationChargeRate,
    decimal DocStampRate,
    decimal NotarialFee,
    decimal InsuranceRate);

/// <summary>Product-level computation config, assembled from LoanProduct + workflow.</summary>
public record LoanProductComputationConfig(
    string ProductCode,
    decimal AnnualInterestRate,
    int TermDays,
    AmortizationMode AmortizationMode,
    bool ChargeAdvanceInterest,
    FeeSchedule Fees,
    IReadOnlyList<MinimumAmortizationTier>? MinimumAmortizationTiers = null)
{
    /// <summary>
    /// Build from a LoanProduct entity (the DB row). The loan's own
    /// interestRate and termDays are passed separately because they
    /// override the product defaults (the product provides the fee
    /// schedule and amortization mode; the loan provides the specific
    /// term/rate for this application).
    /// </summary>
    public static LoanProductComputationConfig FromEntity(
        LoanProduct product,
        decimal interestRate,
        int termDays) => new(
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
            InsuranceRate: product.InsuranceFee > 0 ? product.InsuranceFee : 0m));
}

public enum AmortizationMode { DIM, MIC }

public record MinimumAmortizationTier(decimal From, decimal To, decimal MinimumAmortization);

/// <summary>Resolved fees for a specific proposed amount (may include AO overrides).</summary>
public record LoanFees(
    decimal ApplicationCharge,
    decimal DocStamp,
    decimal NotarialFee,
    decimal Insurance,
    decimal AdvanceInterest);

public record ObligationRow(decimal Deductions, decimal OutstandingBalance);

/// <summary>
/// All inputs required for a full loan computation. Assembled by the
/// endpoint from request DTO + product config + workflow settings.
/// </summary>
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

// ─── Results ────────────────────────────────────────────────────────────

/// <summary>
/// Full set of derived loan metrics. Snapshot columns
/// (TotalDeductions, GrossProceeds, MonthlyAmortization, TotalExposure,
/// MaximumLoanableAmount, NetProceedsToClient) are persisted on the
/// LoanApplication entity — never read from the request.
/// </summary>
public record LoanComputationResults(
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

// ─── Service contract ───────────────────────────────────────────────────

public interface ILoanComputationService
{
    /// <summary>
    /// Compute the expected (policy-default) fees for a product and
    /// proposed amount. AO overrides are applied by the caller after
    /// this returns.
    /// </summary>
    LoanFees ComputeExpectedFees(LoanProductComputationConfig product, decimal proposedAmount);

    /// <summary>
    /// Full loan metrics computation. Pure math — no I/O, no side effects.
    /// Registered as singleton (stateless).
    /// </summary>
    LoanComputationResults ComputeLoanMetrics(LoanComputationInput input);
}

// ─── Implementation ─────────────────────────────────────────────────────

/// <summary>
/// Authoritative loan computation engine. Mirrors the LAM Excel workbook
/// formulas exactly (lam_A16.xlsx).
///
/// Key design decisions:
///   • Excel ROUND parity (half-away-from-zero) on all rounding.
///   • Integer-exponent decimal power (exponentiation by squaring) for
///     the annuity factor — no double conversion, no precision loss.
///   • Closed-form max-loanable: netDisposable / factor (O(1)).
///   • Stateless — safe as singleton.
/// </summary>
public sealed class LoanComputationService : ILoanComputationService
{
    public LoanFees ComputeExpectedFees(LoanProductComputationConfig product, decimal proposedAmount)
        => new(
            ApplicationCharge: Round(proposedAmount * product.Fees.ApplicationChargeRate),
            DocStamp: Round(proposedAmount * product.Fees.DocStampRate),
            NotarialFee: product.Fees.NotarialFee,
            Insurance: Round(proposedAmount * product.Fees.InsuranceRate),
            AdvanceInterest: product.ChargeAdvanceInterest
                ? Round(proposedAmount * product.AnnualInterestRate * product.TermDays / 360m)
                : 0m);

    public LoanComputationResults ComputeLoanMetrics(LoanComputationInput input)
    {
        var proposed = input.ProposedAmount;
        var product = input.Product;

        // ── Row 1–4: Deductions ──────────────────────────────────────
        var totalDeductions = Round(
            input.Fees.ApplicationCharge
            + input.Fees.DocStamp
            + input.Fees.NotarialFee
            + input.Fees.Insurance
            + input.Fees.AdvanceInterest);

        // ── Row 5: Gross Proceeds ────────────────────────────────────
        var grossProceeds = Round(proposed - totalDeductions);

        // ── Row 6–7: Net Proceeds ────────────────────────────────────
        var totalAccountsBalance = Round(Sum(input.Reloans.Select(r => r.OutstandingBalance)));
        var netProceedsOnDS = Round(grossProceeds - totalAccountsBalance);
        var totalBuyOutBalance = Round(Sum(input.BuyOuts.Select(b => b.OutstandingBalance)));
        var netProceedsToClient = Round(netProceedsOnDS - totalBuyOutBalance);

        // ── Row 8–9: Term & Amortization ─────────────────────────────
        var termMonths = product.TermDays / 30m;
        var factor = AnnuityFactor(product.AnnualInterestRate / 12m, termMonths);
        var diminishingAmortization = Round(proposed * factor);

        var minimumAmortization = product.AmortizationMode == AmortizationMode.MIC
            ? product.MinimumAmortizationTiers?
                  .FirstOrDefault(t => proposed >= t.From && proposed <= t.To)?.MinimumAmortization ?? 0m
            : 0m;

        var monthlyAmortization = Math.Max(diminishingAmortization, minimumAmortization);

        // ── Row 10: Total Exposure ───────────────────────────────────
        var totalExposure = Round(proposed + Sum(input.OutstandingPrincipalBalances));

        // ── Row 11–14: Disposable Income Chain ───────────────────────
        var releasedDeductions = Round(
            Sum(input.Reloans.Select(r => r.Deductions))
            + Sum(input.BuyOuts.Select(b => b.Deductions)));

        var netPayAfterDeduction = Round(input.NetTakeHomePay - monthlyAmortization + releasedDeductions);
        var grossDisposableIncome = Round(input.NetTakeHomePay + releasedDeductions);
        var capacityDeductions = Round(input.MinimumNthp + Sum(input.IncomingDeductions));
        var netDisposableIncome = Round(grossDisposableIncome - capacityDeductions);

        // ── Row 15: Maximum Loanable Amount (closed-form) ────────────
        var maximumLoanableAmount = factor > 0 ? Round(netDisposableIncome / factor) : 0m;

        // ── Row 16: Gates ────────────────────────────────────────────
        var amortizationExceedsDisposable = monthlyAmortization > netDisposableIncome;
        var nthpBelowMinimum = input.NetTakeHomePay < input.MinimumNthp;

        return new LoanComputationResults(
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

    // ─── Annuity factor ──────────────────────────────────────────────

    /// <summary>
    /// Standard annuity factor: i(1+i)^n / ((1+i)^n - 1).
    /// Uses <see cref="DecimalPow"/> for exact decimal exponentiation.
    /// </summary>
    public static decimal AnnuityFactor(decimal monthlyRate, decimal termMonths)
    {
        if (termMonths <= 0) return 0m;
        if (monthlyRate == 0m) return 1m / termMonths;

        var pow = DecimalPow(1m + monthlyRate, (int)termMonths);
        return monthlyRate * pow / (pow - 1m);
    }

    /// <summary>
    /// Exact decimal power for whole-month exponents via exponentiation
    /// by squaring. No double conversion, no precision loss.
    /// ~10 multiplications for 84 months.
    /// </summary>
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

    // ─── Helpers ─────────────────────────────────────────────────────

    private static decimal Sum(IEnumerable<decimal> values) => values.Sum();

    /// <summary>
    /// Excel ROUND parity: half-away-from-zero at 2 decimal places.
    /// If Compliance later mandates banker's rounding, change this
    /// single helper and regenerate golden fixtures.
    /// </summary>
    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

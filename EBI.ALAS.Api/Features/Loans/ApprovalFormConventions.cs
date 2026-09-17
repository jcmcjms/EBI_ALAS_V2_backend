namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Server mirror of the frontend's approval-form boundary rules
/// (src/lib/loan-computations.ts → resolveApprovalTermDays, and
/// src/pages/loans/create/components/approval-form-preview.tsx → toAnnualRatePercent).
/// The two implementations are kept identical by a shared xUnit/Vitest case
/// table — do not "fix" one side without the other.
/// </summary>
public static class ApprovalFormConventions
{
    /// <summary>Legacy "1 month = 30 days" convention used by the LAM template.</summary>
    public const int DaysPerMonth = 30;

    /// <summary>
    /// Single-payment products (policy count = 1) diverge from the real term
    /// by far more than any grace period; quote feed days verbatim when the
    /// gap exceeds this tolerance.
    /// </summary>
    public const int GraceToleranceDays = 120;

    /// <summary>
    /// Resolves the TERM (Days) value printed on the approval form.
    /// When <paramref name="policyTermMonths"/> is present and its 30-day
    /// equivalent is within <see cref="GraceToleranceDays"/> of the feed
    /// term, the policy-derived value wins (it's the "clean" amortization
    /// period). Otherwise the raw feed days are quoted verbatim (single-
    /// payment products, or missing policy data).
    /// </summary>
    /// <param name="feedTermDays">webloan loan_data term days (calendar day-count to maturity).</param>
    /// <param name="policyTermMonths">webloan loan_data.total_amortization (amortization period count, e.g. 84).</param>
    /// <returns>The resolved term in days for the printed approval form.</returns>
    public static int ResolveApprovalTermDays(int feedTermDays, int? policyTermMonths)
    {
        if (policyTermMonths is not { } months || months <= 0)
            return feedTermDays;

        var policyDays = months * DaysPerMonth;
        return Math.Abs(policyDays - feedTermDays) <= GraceToleranceDays
            ? policyDays
            : feedTermDays;
    }

    /// <summary>
    /// WebLoan ships granted_rate as a decimal fraction (0.2157); the approval
    /// form is parameterized in percent (21.57). Idempotent for values already
    /// in percent. No consumer product prices at ≤ 1% p.a., so the threshold
    /// is unambiguous.
    /// </summary>
    /// <param name="rate">Raw rate from webloan (fraction or percent).</param>
    /// <returns>Annual rate in percent (e.g. 21.57).</returns>
    public static decimal ToAnnualRatePercent(decimal rate) =>
        rate > 0 && rate <= 1 ? rate * 100 : rate;
}

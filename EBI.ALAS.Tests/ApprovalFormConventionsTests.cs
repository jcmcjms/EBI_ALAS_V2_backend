using EBI.ALAS.Api.Features.Loans;

namespace EBI.ALAS.Tests;

/// <summary>
/// Shared case table for approval-form convention rules. These tests are
/// the xUnit mirror of the Vitest cases in loan-approval-utils.spec.ts.
/// Do not "fix" one side without the other — the two implementations must
/// produce identical results for every row in this table.
/// </summary>
public class ApprovalFormConventionsTests
{
    // ── ResolveApprovalTermDays ─────────────────────────────────────────

    [Theory]
    [InlineData(2572, 84, 2520)]  // Your bug: grace 52d ≤ 120 → policy wins
    [InlineData(720, 1, 720)]     // C02 single-payment: 30d vs 720d → feed verbatim
    [InlineData(2520, 84, 2520)]  // Idempotent
    [InlineData(900, null, 900)]  // No policy → feed
    public void ResolveApprovalTermDays_ReturnsExpected(
        int feedTermDays, int? policyTermMonths, int expected)
    {
        var result = ApprovalFormConventions.ResolveApprovalTermDays(feedTermDays, policyTermMonths);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveApprovalTermDays_ZeroPolicyTermMonths_ReturnsFeed()
    {
        Assert.Equal(1000, ApprovalFormConventions.ResolveApprovalTermDays(1000, 0));
    }

    [Fact]
    public void ResolveApprovalTermDays_NegativePolicyTermMonths_ReturnsFeed()
    {
        Assert.Equal(1000, ApprovalFormConventions.ResolveApprovalTermDays(1000, -5));
    }

    [Fact]
    public void ResolveApprovalTermDays_GraceExactlyAtBoundary_ReturnsPolicy()
    {
        // 84 * 30 = 2520, feed = 2640, grace = 120 (exactly at boundary)
        Assert.Equal(2520, ApprovalFormConventions.ResolveApprovalTermDays(2640, 84));
    }

    [Fact]
    public void ResolveApprovalTermDays_GraceJustOverBoundary_ReturnsFeed()
    {
        // 84 * 30 = 2520, feed = 2641, grace = 121 (just over boundary)
        Assert.Equal(2641, ApprovalFormConventions.ResolveApprovalTermDays(2641, 84));
    }

    [Fact]
    public void ResolveApprovalTermDays_FeedLessThanPolicy_WithinGrace_ReturnsPolicy()
    {
        // 84 * 30 = 2520, feed = 2400, |grace| = 120 (within tolerance) → policy wins
        Assert.Equal(2520, ApprovalFormConventions.ResolveApprovalTermDays(2400, 84));
    }

    [Fact]
    public void ResolveApprovalTermDays_FeedLessThanPolicy_OutsideGrace_ReturnsFeed()
    {
        // 84 * 30 = 2520, feed = 2399, |grace| = 121 (outside tolerance) → feed verbatim
        Assert.Equal(2399, ApprovalFormConventions.ResolveApprovalTermDays(2399, 84));
    }

    // ── ToAnnualRatePercent ─────────────────────────────────────────────

    [Theory]
    [InlineData(0.2157, 21.57)]   // Fraction → percent
    [InlineData(21.57, 21.57)]    // Already in percent (idempotent)
    [InlineData(0, 0)]            // Zero
    public void ToAnnualRatePercent_ReturnsExpected(decimal rate, decimal expected)
    {
        var result = ApprovalFormConventions.ToAnnualRatePercent(rate);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToAnnualRatePercent_ExactOne_ReturnsHundred()
    {
        // rate = 1.0 is treated as fraction (1%)
        Assert.Equal(100m, ApprovalFormConventions.ToAnnualRatePercent(1.0m));
    }

    [Fact]
    public void ToAnnualRatePercent_JustAboveOne_ReturnsVerbatim()
    {
        // rate = 1.0001 is already in percent
        Assert.Equal(1.0001m, ApprovalFormConventions.ToAnnualRatePercent(1.0001m));
    }

    [Fact]
    public void ToAnnualRatePercent_NegativeRate_ReturnsVerbatim()
    {
        // Negative rates pass through unchanged
        Assert.Equal(-5m, ApprovalFormConventions.ToAnnualRatePercent(-5m));
    }

    // ── Constants ───────────────────────────────────────────────────────

    [Fact]
    public void DaysPerMonth_IsThirty()
    {
        Assert.Equal(30, ApprovalFormConventions.DaysPerMonth);
    }

    [Fact]
    public void GraceToleranceDays_IsOneHundredTwenty()
    {
        Assert.Equal(120, ApprovalFormConventions.GraceToleranceDays);
    }
}

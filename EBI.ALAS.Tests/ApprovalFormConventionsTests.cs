using EBI.ALAS.Api.Features.Loans;
namespace EBI.ALAS.Tests;
public class ApprovalFormConventionsTests
{
    [Theory]
    [InlineData(2572, 84, 2520)]
    [InlineData(720, 1, 720)]
    [InlineData(2520, 84, 2520)]
    [InlineData(900, null, 900)]
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
        Assert.Equal(2520, ApprovalFormConventions.ResolveApprovalTermDays(2640, 84));
    }
    [Fact]
    public void ResolveApprovalTermDays_GraceJustOverBoundary_ReturnsFeed()
    {
        Assert.Equal(2641, ApprovalFormConventions.ResolveApprovalTermDays(2641, 84));
    }
    [Fact]
    public void ResolveApprovalTermDays_FeedLessThanPolicy_WithinGrace_ReturnsPolicy()
    {
        Assert.Equal(2520, ApprovalFormConventions.ResolveApprovalTermDays(2400, 84));
    }
    [Fact]
    public void ResolveApprovalTermDays_FeedLessThanPolicy_OutsideGrace_ReturnsFeed()
    {
        Assert.Equal(2399, ApprovalFormConventions.ResolveApprovalTermDays(2399, 84));
    }
    [Theory]
    [InlineData(0.2157, 21.57)]
    [InlineData(21.57, 21.57)]
    [InlineData(0, 0)]
    public void ToAnnualRatePercent_ReturnsExpected(decimal rate, decimal expected)
    {
        var result = ApprovalFormConventions.ToAnnualRatePercent(rate);
        Assert.Equal(expected, result);
    }
    [Fact]
    public void ToAnnualRatePercent_ExactOne_ReturnsHundred()
    {
        Assert.Equal(100m, ApprovalFormConventions.ToAnnualRatePercent(1.0m));
    }
    [Fact]
    public void ToAnnualRatePercent_JustAboveOne_ReturnsVerbatim()
    {
        Assert.Equal(1.0001m, ApprovalFormConventions.ToAnnualRatePercent(1.0001m));
    }
    [Fact]
    public void ToAnnualRatePercent_NegativeRate_ReturnsVerbatim()
    {
        Assert.Equal(-5m, ApprovalFormConventions.ToAnnualRatePercent(-5m));
    }
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

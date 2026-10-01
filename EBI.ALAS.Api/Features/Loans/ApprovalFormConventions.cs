namespace EBI.ALAS.Api.Features.Loans;
public static class ApprovalFormConventions
{
    public const int DaysPerMonth = 30;
    public const int GraceToleranceDays = 120;
    public static int ResolveApprovalTermDays(int feedTermDays, int? policyTermMonths)
    {
        if (policyTermMonths is not { } months || months <= 0)
            return feedTermDays;
        var policyDays = months * DaysPerMonth;
        return Math.Abs(policyDays - feedTermDays) <= GraceToleranceDays
            ? policyDays
            : feedTermDays;
    }
    public static decimal ToAnnualRatePercent(decimal rate) =>
        rate > 0 && rate <= 1 ? rate * 100 : rate;
}

namespace EBI.ALAS.Api.Features.ApprovalMatrix;
public enum LoanCycle { New = 0, Renewal = 1 }
public sealed record RoutingInputs(
    LoanCycle Cycle,
    DeviationSeverity Severity,
    decimal Exposure);
public static class ApprovalCycleResolver
{
    public static ApprovalAuthority? Match(
        IReadOnlyList<ApprovalAuthority> ladder,
        RoutingInputs inputs)
    {
        foreach (var rule in ladder)
        {
            var cycleAllowed = inputs.Cycle == LoanCycle.Renewal
                ? rule.AllowRenewal
                : rule.AllowNew;
            if (!cycleAllowed)
                continue;
            if (rule.MaxSeverity < inputs.Severity)
                continue;
            if (inputs.Exposure > rule.MaxTotalExposure)
                continue;
            return rule;
        }
        return null;
    }
}

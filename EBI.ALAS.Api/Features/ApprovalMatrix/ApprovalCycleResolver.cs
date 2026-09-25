namespace EBI.ALAS.Api.Features.ApprovalMatrix;

public enum LoanCycle { New = 0, Renewal = 1 }

public sealed record RoutingInputs(
    LoanCycle Cycle,
    DeviationSeverity Severity,
    decimal Exposure);

/// <summary>
/// Pure-function resolver that evaluates the ApprovalAuthority ladder
/// as data-driven ordered rules. No DB calls, no caching — the caller
/// passes pre-loaded data.
/// </summary>
public static class ApprovalCycleResolver
{
    /// <summary>
    /// Returns the lowest-tier authority that matches the routing inputs,
    /// or null when no rule qualifies (exposure exceeds all tiers, or
    /// cycle/severity has no covering rule).
    /// </summary>
    public static ApprovalAuthority? Match(
        IReadOnlyList<ApprovalAuthority> ladder,
        RoutingInputs inputs)
    {
        // Ladder must be ordered by Tier, then Priority.
        // We only need the first match per tier (lowest priority wins
        // within a tier, but the resolver returns the authority row for
        // display purposes — the assignment service picks the actual user).
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
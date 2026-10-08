namespace Ebi.Alas.Api.Features.Approvals;

public sealed record RoutingRequest(
    decimal TotalExposure,
    bool IsReloan,
    int MaxDeviationSeverity,
    string BranchId,
    string? AreaKey);

public sealed class ApprovalRoutingService
{
    public RoutingDecision Route(RoutingRequest request, IReadOnlyList<ApprovalAuthority> authorities)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authorities);

        var requiredTier = ComputeRequiredTier(request);
        var match = authorities
            .Where(a => a.Tier >= requiredTier)
            .Where(a => request.IsReloan ? a.AllowRenewal : a.AllowNew)
            .Where(a => a.MaxSeverity >= request.MaxDeviationSeverity)
            .Where(a => a.MaxTotalExposure >= request.TotalExposure)
            .Where(a => CoversScope(a, request))
            .OrderBy(a => a.Tier)
            .FirstOrDefault();

        return match is null
            ? new RoutingDecision(requiredTier, null, null, true, "No staffed authority covers the required tier.")
            : new RoutingDecision(requiredTier, match.Id, null, match.Tier > requiredTier, match.Tier > requiredTier
                ? "Escalated to a higher tier."
                : null);
    }

    private static int ComputeRequiredTier(RoutingRequest request)
    {
        if (request.TotalExposure >= 1_000_000m || request.MaxDeviationSeverity >= 3)
        {
            return 3;
        }

        if (request.TotalExposure >= 250_000m || request.MaxDeviationSeverity >= 2)
        {
            return 2;
        }

        return 1;
    }

    private static bool CoversScope(ApprovalAuthority authority, RoutingRequest request) =>
        authority.ScopeType switch
        {
            ApprovalScope.Global => true,
            ApprovalScope.Branch => authority.ScopeKey == request.BranchId,
            ApprovalScope.Area => authority.ScopeKey == request.AreaKey,
            _ => false
        };
}

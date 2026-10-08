namespace Ebi.Alas.Api.Features.Approvals;

public enum ApprovalScope
{
    Branch = 1,
    Area = 2,
    Global = 3
}

public sealed record ApprovalAuthority(
    Guid Id,
    int Tier,
    bool AllowNew,
    bool AllowRenewal,
    int MaxSeverity,
    decimal MaxTotalExposure,
    ApprovalScope ScopeType,
    string ScopeKey);

public sealed record RoutingDecision(
    int RequiredTier,
    Guid? MatchedAuthorityId,
    Guid? AssignedApproverUserId,
    bool IsEscalated,
    string? Reason);

namespace Ebi.Alas.Api.Features.Approvals;

public sealed record AssignedApproverResponse(string UserId, string Name);

public sealed record ApprovalAuthorityResponse(
    string Key,
    string DisplayName,
    int Tier,
    int Priority,
    bool AllowNew,
    bool AllowRenewal,
    int MaxSeverity,
    decimal MaxTotalExposure,
    int ScopeType,
    IReadOnlyList<AssignedApproverResponse> AssignedUsers);

public sealed record CreateApprovalAuthorityRequest(
    string DisplayName,
    int Tier,
    int Priority,
    bool AllowNew,
    bool AllowRenewal,
    int MaxSeverity,
    decimal MaxTotalExposure,
    int ScopeType);

public sealed record UpdateApprovalAuthorityRequest(
    string DisplayName,
    int Tier,
    int Priority,
    bool AllowNew,
    bool AllowRenewal,
    int MaxSeverity,
    decimal MaxTotalExposure,
    int ScopeType);

public sealed record SetApprovalAuthorityUsersRequest(IReadOnlyList<string> UserIds);

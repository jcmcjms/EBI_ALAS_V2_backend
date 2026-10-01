namespace EBI.ALAS.Api.Features.Users;
public record UserQueryParameters(
    string? Search,
    string? Role,
    string? BranchId,
    bool? IsActive,
    int PageNumber = 1,
    int PageSize = 20
);
public record CreateUserRequest(
    string Username,
    string Password,
    string FirstName,
    string? MiddleName,
    string LastName,
    string BranchId,
    string Role,
    string? JobTitle = null,
    string? ESignature = null,
    IReadOnlyList<string>? CoveredBranches = null
);
public record UpdateUserRequest(
    string FirstName,
    string? MiddleName,
    string LastName,
    string BranchId,
    string Role,
    string? JobTitle = null,
    string? ESignature = null,
    IReadOnlyList<string>? CoveredBranches = null
);
public record UserStatusRequest(bool IsActive);
public record ResetPasswordRequest(string? NewPassword = null);
public sealed record ResetPasswordResponse(string Username, string TemporaryPassword, bool MustChangePassword);
public record UserAuditLogResponse(
    int Id,
    string Action,
    string EntityType,
    string EntityLabel,
    string Summary,
    DateTime Timestamp,
    string? IpAddress
);
public record ApprovalAuthorityInfo(
    string Key,
    string DisplayName,
    int Tier,
    int Priority,
    decimal MaxTotalExposure
);
public record UserResponse(
    int Id,
    string Username,
    string FirstName,
    string? MiddleName,
    string LastName,
    string BranchId,
    string Role,
    bool IsActive,
    DateTime CreatedAt,
    string? JobTitle = null,
    string? ESignature = null,
    ApprovalAuthorityInfo? ApprovalAuthority = null,
    IReadOnlyList<string>? CoveredBranches = null
);

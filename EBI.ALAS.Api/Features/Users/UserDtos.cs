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
    // null  = no change (keep current value)
    // ""    = clear the signature
    // value = replace with the new base64 PNG
    string? ESignature = null,
    IReadOnlyList<string>? CoveredBranches = null
);

public record UserStatusRequest(bool IsActive);

public record ResetPasswordRequest(string? NewPassword = null);

/// <summary>
/// Response from the reset-password endpoint. The temporary password is
/// shown exactly once — in the secure handoff dialog — and never logged.
/// </summary>
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

/// <summary>
/// Lightweight representation of an ApprovalAuthority for embedding in
/// UserResponse. Keeps the routing key, display label, and tier info
/// so the admin UI can render the authority dropdown without a second
/// fetch.
/// </summary>
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

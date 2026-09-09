namespace EBI.ALAS.Api.Features.Users;

public record UserQueryParameters(
    string? Search,
    string? Role,
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
    string? ESignature = null
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
    string? ESignature = null
);

public record UserStatusRequest(bool IsActive);

public record ResetPasswordRequest(string NewPassword);

public record UserAuditLogResponse(
    int Id,
    string Action,
    string EntityType,
    string EntityLabel,
    string Summary,
    DateTime Timestamp,
    string? IpAddress
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
    string? ESignature = null
);

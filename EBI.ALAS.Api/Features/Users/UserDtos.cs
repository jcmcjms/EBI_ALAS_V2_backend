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
    string Role
);

public record UpdateUserRequest(
    string FirstName,
    string? MiddleName,
    string LastName,
    string BranchId,
    string Role
);

public record UserStatusRequest(bool IsActive);

public record UserResponse(
    int Id,
    string Username,
    string FirstName,
    string? MiddleName,
    string LastName,
    string BranchId,
    string Role,
    bool IsActive,
    DateTime CreatedAt
);

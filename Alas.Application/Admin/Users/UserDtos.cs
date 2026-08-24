namespace Alas.Application.Admin.Users;

public sealed record UserListItemDto(
    Guid UserId,
    string Username,
    string FullName,
    string? Email,
    string? BranchId,
    bool IsActive,
    IReadOnlyCollection<string> Roles,
    DateTimeOffset CreatedUtc);

public sealed record UserDetailDto(
    Guid UserId,
    string Username,
    string FullName,
    string? Email,
    string? BranchId,
    bool IsActive,
    int PermissionVersion,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions,
    DateTimeOffset CreatedUtc);

public sealed record CreateUserRequest(
    string Username,
    string Password,
    string FullName,
    string? Email,
    string? BranchId);

public sealed record UpdateUserStatusRequest(
    bool IsActive);

public sealed record AssignRolesRequest(
    IReadOnlyCollection<string> Roles);

public sealed record PagedResult<T>(
    IReadOnlyCollection<T> Items,
    int TotalCount,
    int Page,
    int PageSize);

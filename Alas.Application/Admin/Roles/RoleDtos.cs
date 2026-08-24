namespace Alas.Application.Admin.Roles;

public sealed record RoleListItemDto(
    Guid RoleId,
    string Name,
    string? Description,
    int UserCount);

public sealed record RoleDetailDto(
    Guid RoleId,
    string Name,
    string? Description,
    IReadOnlyCollection<string> Permissions,
    int UserCount);

public sealed record CreateRoleRequest(
    string Name,
    string? Description);

public sealed record AssignPermissionsRequest(
    IReadOnlyCollection<string> Permissions);

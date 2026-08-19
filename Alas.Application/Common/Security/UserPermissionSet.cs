namespace Alas.Application.Common.Security;

public sealed record UserPermissionSet(
    Guid UserId,
    bool IsActive,
    int PermissionVersion,
    IReadOnlySet<string> Permissions);
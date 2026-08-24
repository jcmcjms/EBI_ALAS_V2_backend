namespace Alas.Application.Common.Security;

public sealed record AuthUserDto(
    string UserId,
    string Username,
    string FullName,
    string BranchId,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);

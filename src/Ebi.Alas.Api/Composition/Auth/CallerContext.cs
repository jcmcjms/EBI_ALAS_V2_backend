using System.Security.Claims;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.Users.Domain;

namespace Ebi.Alas.Api.Composition.Auth;

public sealed record CallerContext(Guid UserId, UserRole Role, string BranchId)
{
    public bool IsAdmin => Role == UserRole.Admin;

    public bool IsSystem => Role == UserRole.System;

    public bool CanAccessAllBranches => IsAdmin || IsSystem;

    public static CallerContext Require(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId) || userId == Guid.Empty)
        {
            throw new ForbiddenException("Invalid user identity.");
        }

        var roleValue = principal.FindFirstValue(ClaimTypes.Role)
            ?? principal.FindFirstValue("role");
        if (!Enum.TryParse<UserRole>(roleValue, ignoreCase: true, out var role))
        {
            throw new ForbiddenException("Invalid user role.");
        }

        var branchId = principal.FindFirstValue("branch");
        if (string.IsNullOrWhiteSpace(branchId))
        {
            throw new ForbiddenException("Invalid user branch.");
        }

        return new CallerContext(userId, role, branchId.Trim());
    }
}

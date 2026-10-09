using System.Security.Claims;

namespace Ebi.Alas.Api.Features.AuditLogs;

public static class AuditActor
{
    public static Guid? GetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public static string GetUsername(this ClaimsPrincipal user)
        => user.Identity?.Name
            ?? user.FindFirstValue("username")
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? string.Empty;
}

using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.Presence;
namespace EBI.ALAS.Api.Features.Presence;
public static class PresenceEndpoints
{
    public static void MapPresenceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/presence")
            .WithTags("Presence")
            .RequireAuthorization();
        group.MapGet("/online", (IPresenceService presence, ClaimsPrincipal principal) =>
        {
            var role = principal.GetRole();
            var branchCode = principal.GetBranchCode();
            var all = presence.OnlineUsers();
            if (role != Roles.Admin && !string.IsNullOrEmpty(branchCode))
                all = all.Where(e => e.User.BranchCode == branchCode).ToList();
            return Results.Ok(ApiResponse<IReadOnlyList<PresenceEntry>>.SuccessResponse(all));
        })
        .WithName("GetOnlineUsers")
        .Produces<ApiResponse<IReadOnlyList<PresenceEntry>>>();
        group.MapGet("/", (string? userIds, IPresenceService presence, ClaimsPrincipal principal) =>
        {
            var role = principal.GetRole();
            var ids = (userIds ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var i) ? i : (int?)null)
                .Where(i => i.HasValue)
                .Select(i => i!.Value)
                .Distinct()
                .Take(200)
                .ToList();
            var flags = ids.Select(id => new
            {
                userId = id,
                online = presence.IsOnline(id),
                connections = role == Roles.Admin ? presence.ConnectionCount(id) : (int?)null
            });
            return Results.Ok(ApiResponse<object>.SuccessResponse(flags));
        })
        .WithName("GetPresenceFlags")
        .Produces<ApiResponse<object>>();
    }
}

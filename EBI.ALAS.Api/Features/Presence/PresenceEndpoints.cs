using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.Presence;

namespace EBI.ALAS.Api.Features.Presence;

/// <summary>
/// General-purpose presence REST endpoints.
///
/// These complement the real-time SignalR presence by providing
/// HTTP-accessible snapshots for:
///   • Full online directory (header who's-online popover).
///   • Batch liveness flags for user tables that also show offline users.
///
/// The <c>/api/presence/approvers</c> endpoint remains in
/// <see cref="Loans.LoanEndpoints.MapApprovalMatrixEndpoints"/> as a
/// composition of presence + lease/reviewing state.
/// </summary>
public static class PresenceEndpoints
{
    public static void MapPresenceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/presence")
            .WithTags("Presence")
            .RequireAuthorization();

        // Full online directory — scoped to caller's branch for non-admins.
        group.MapGet("/online", (IPresenceService presence, ClaimsPrincipal principal) =>
        {
            var role = principal.GetRole();
            var branchCode = principal.GetBranchCode();

            var all = presence.OnlineUsers();

            // Non-admins only see users from their own branch.
            // Admins see everyone (they manage the full org).
            if (role != Roles.Admin && !string.IsNullOrEmpty(branchCode))
                all = all.Where(e => e.User.BranchCode == branchCode).ToList();

            return Results.Ok(ApiResponse<IReadOnlyList<PresenceEntry>>.SuccessResponse(all));
        })
        .WithName("GetOnlineUsers")
        .Produces<ApiResponse<IReadOnlyList<PresenceEntry>>>();

        // Batch liveness for tables that also show offline users.
        // Accepts comma-separated userIds, returns online/connection-count flags.
        // Connection count is only visible to admins (behavioral metadata).
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
                // Connection count is behavioral metadata — only admins see it.
                connections = role == Roles.Admin ? presence.ConnectionCount(id) : (int?)null
            });

            return Results.Ok(ApiResponse<object>.SuccessResponse(flags));
        })
        .WithName("GetPresenceFlags")
        .Produces<ApiResponse<object>>();
    }
}

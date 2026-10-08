using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;

namespace Ebi.Alas.Api.Features.Presence;

public static class PresenceEndpoints
{
    public static void MapPresenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/presence/heartbeat", async (
            PresenceService presence,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            await presence.HeartbeatAsync(caller.UserId, cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags("Presence");

        endpoints.MapGet("/api/presence/online", async (
            PresenceService presence,
            CancellationToken cancellationToken) =>
        {
            var ids = await presence.GetOnlineUserIdsAsync(cancellationToken);
            return Results.Ok(ids);
        })
        .RequireAuthorization()
        .WithTags("Presence");
    }
}

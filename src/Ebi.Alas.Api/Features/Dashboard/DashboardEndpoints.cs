using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;

namespace Ebi.Alas.Api.Features.Dashboard;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dashboard/overview", async (
            DashboardService service,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var result = await service.GetOverviewAsync(caller, cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization()
        .WithTags("Dashboard");
    }
}

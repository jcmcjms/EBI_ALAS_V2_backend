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
            string? date,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var day = DateOnly.TryParse(date, out var parsed)
                ? parsed
                : DashboardService.ManilaToday();
            var result = await service.GetOverviewAsync(caller, day, cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization()
        .WithTags("Dashboard");
    }
}

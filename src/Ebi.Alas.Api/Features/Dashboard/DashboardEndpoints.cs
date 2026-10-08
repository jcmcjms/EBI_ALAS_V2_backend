using Ebi.Alas.Api.Features.Dashboard;

namespace Ebi.Alas.Api.Features.Dashboard;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dashboard/overview", async (
            DashboardService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetOverviewAsync(cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization()
        .WithTags("Dashboard");
    }
}

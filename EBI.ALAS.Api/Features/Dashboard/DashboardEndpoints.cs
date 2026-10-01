using System.Security.Claims;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
namespace EBI.ALAS.Api.Features.Dashboard;
public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/dashboard")
            .WithTags("Dashboard")
            .RequireAuthorization();
        group.MapGet("/overview", async (
            IDashboardService dashboardService,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var overview = await dashboardService.GetOverviewAsync(
                user.GetBranchId(), user.GetRole(), ct);
            return Results.Ok(ApiResponse<DashboardOverviewResponse>.SuccessResponse(overview));
        })
        .WithName("GetDashboardOverview")
        .Produces<ApiResponse<DashboardOverviewResponse>>(200)
        .Produces<ApiResponse>(401)
        .RequireAuthorization("CanViewLoan");
    }
}

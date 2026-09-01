using System.Security.Claims;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using Microsoft.AspNetCore.Authorization;

namespace EBI.ALAS.Api.Features.Dashboard;
public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/dashboard")
            .WithTags("Dashboard")
            .RequireAuthorization();

        // GET /api/dashboard/summary
        group.MapGet("/summary", async (
            IDashboardService dashboardService,
            ClaimsPrincipal user) =>
        {
            var branchId = user.GetBranchId();
            var role = user.GetRole();

            var summary = await dashboardService.GetSummaryAsync(branchId, role);

            return Results.Ok(ApiResponse<DashboardSummaryResponse>.SuccessResponse(summary));
        })
        .WithName("GetDashboardSummary")
        .Produces<ApiResponse<DashboardSummaryResponse>>(200);
    }
}

using System.Security.Claims;
using Ebi.Alas.Api.Composition;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Pagination;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Microsoft.Extensions.Options;

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

        endpoints.MapGet("/api/dashboard/pending-queue", async (
            string status,
            int? page,
            int? pageSize,
            DashboardService service,
            ClaimsPrincipal user,
            IOptions<ApiOptions> options,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            if (!Enum.TryParse<LoanStatus>(status, ignoreCase: true, out var parsed))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Unknown loan status."],
                });
            }

            var opt = options.Value;
            var result = await service.GetPendingQueueAsync(
                caller,
                parsed,
                new PageRequest { Page = page ?? 1, PageSize = pageSize ?? opt.DefaultPageSize },
                opt.MaxPageSize,
                opt.DefaultPageSize,
                cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization()
        .WithTags("Dashboard");
    }
}

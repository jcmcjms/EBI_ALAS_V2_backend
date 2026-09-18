using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Features.Loans.Endpoints;

public static class GetQueueDefault
{
    public static void MapGetQueueDefaultEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loans")
            .RequireAuthorization();

        group.MapGet("/queue-default", (ClaimsPrincipal user) =>
            Results.Ok(ApiResponse<List<string>>.SuccessResponse(
                RoleQueues.DefaultStatusesFor(user.GetRole()).ToList())))
        .WithName("GetQueueDefault")
        .Produces<ApiResponse<List<string>>>(200)
        .RequireAuthorization("CanViewLoan");
    }
}

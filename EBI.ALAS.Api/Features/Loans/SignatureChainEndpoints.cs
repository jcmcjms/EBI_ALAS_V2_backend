using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

public static class SignatureChainEndpoints
{
    public static IEndpointRouteBuilder MapSignatureChainEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api")
            .WithTags("Loans")
            .RequireAuthorization();

        // Unsigned template — any authenticated officer can preview/print a draft.
        group.MapGet("/workflow/signature-chain", (ISignatureChainService svc) =>
            Results.Ok(ApiResponse<IReadOnlyList<SignatureSlotDto>>.SuccessResponse(svc.GetTemplate())))
        .WithName("GetSignatureChainTemplate")
        .Produces<ApiResponse<IReadOnlyList<SignatureSlotDto>>>(200);

        // Resolved chain for a persisted loan — same two-stage gate as /history.
        group.MapGet("/loans/{id:int}/signature-chain", async (
            int id,
            ISignatureChainService svc,
            AppDbContext context,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var canViewAny = user.HasPermission(Permissions.LoansView);
            var userId = user.GetUserId();

            if (!canViewAny)
            {
                var isOwner = await context.LoanApplications
                    .AsNoTracking()
                    .AnyAsync(l => l.Id == id && l.CreatedById == userId, ct);

                if (!isOwner)
                    return Results.Json(
                        ApiResponse.ErrorResponse("You do not have permission to view this loan's signature chain."),
                        statusCode: StatusCodes.Status403Forbidden);
            }

            var slots = await svc.ResolveForLoanAsync(id, ct);
            return slots is null
                ? Results.NotFound(ApiResponse.ErrorResponse("Loan not found"))
                : Results.Ok(ApiResponse<IReadOnlyList<SignatureSlotDto>>.SuccessResponse(slots));
        })
        .WithName("GetLoanSignatureChain")
        .Produces<ApiResponse<IReadOnlyList<SignatureSlotDto>>>(200)
        .Produces<ApiResponse>(404)
        .Produces<ApiResponse>(403);

        return app;
    }
}

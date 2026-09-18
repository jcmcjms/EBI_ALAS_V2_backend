using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans.Endpoints;

public static class GetLoanHistory
{
    public static void MapGetLoanHistoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loans")
            .RequireAuthorization();

        group.MapGet("/{id:int}/history", async (
            int id,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();

            var loan = await db.LoanApplications
                .AsNoTracking()
                .Where(l => l.Id == id)
                .Select(l => new { l.Id, l.CreatedById })
                .FirstOrDefaultAsync(ct);

            if (loan is null)
                return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));

            var hasViewPerm = principal.HasPermission(Permissions.LoansView);
            var isCreator = loan.CreatedById == userId;
            if (!hasViewPerm && !isCreator)
                return Results.Json(
                    ApiResponse.ErrorResponse("You do not have permission to view this loan's history."),
                    statusCode: StatusCodes.Status403Forbidden);

            var history = await db.LoanActions
                .AsNoTracking()
                .Where(a => a.LoanApplicationId == id)
                .OrderBy(a => a.ActionDate)
                .ThenBy(a => a.Id)
                .Take(500)
                .Select(a => new LoanHistoryEntryResponse(
                    a.Id,
                    $"{a.ActionByUser.FirstName} {a.ActionByUser.LastName}",
                    a.Action,
                    a.FromStatus,
                    a.ToStatus,
                    a.Comments,
                    a.ActionDate,
                    a.ActionByUser.Role))
                .ToListAsync(ct);

            return Results.Ok(ApiResponse<List<LoanHistoryEntryResponse>>.SuccessResponse(history));
        })
        .WithName("GetLoanHistory")
        .Produces<ApiResponse<List<LoanHistoryEntryResponse>>>(200)
        .Produces<ApiResponse>(404)
        .Produces<ApiResponse>(403);
    }
}

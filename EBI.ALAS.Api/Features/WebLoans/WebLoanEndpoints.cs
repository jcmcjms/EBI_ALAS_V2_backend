using EBI.ALAS.Api.Common.Models;
using Microsoft.AspNetCore.Authorization;

namespace EBI.ALAS.Api.Features.WebLoans;

/// <summary>
/// Read-only endpoints for fetching borrower data from the WebLoan system database.
/// </summary>
public static class WebLoanEndpoints
{
    public static void MapWebLoanEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/webloans")
            .WithTags("WebLoans")
            .RequireAuthorization();

        // GET /api/webloans/cis/{cisNo}
        // Returns all webloan data for a borrower, structured per ALAS
        // application sections (personal info, loan info, outstanding loans, reloans).
        group.MapGet("/cis/{cisNo}", async (
            string cisNo,
            IWebLoanService webLoanService) =>
        {
            var borrower = await webLoanService.GetBorrowerByCisAsync(cisNo);

            return borrower is null
                ? Results.NotFound(ApiResponse<WebLoanBorrowerResponse>.ErrorResponse(
                    $"No webloan records found for CIS number '{cisNo}'."))
                : Results.Ok(ApiResponse<WebLoanBorrowerResponse>.SuccessResponse(
                    borrower, "Webloan borrower data retrieved successfully."));
        })
        .WithName("GetWebLoanByCis")
        .Produces<ApiResponse<WebLoanBorrowerResponse>>(200)
        .Produces<ApiResponse<WebLoanBorrowerResponse>>(404);
    }
}

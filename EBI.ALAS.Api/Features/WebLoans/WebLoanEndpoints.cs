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

        // ─── Step 1: Search CIS ────────────────────────────────────────────
        // GET /api/webloans/cis/{cisNo}/search
        // Lightweight search: returns basic borrower info + account list for selection.
        group.MapGet("/cis/{cisNo}/search", async (
            string cisNo,
            IWebLoanService webLoanService) =>
        {
            var result = await webLoanService.SearchCisAsync(cisNo);

            return result is null
                ? Results.NotFound(ApiResponse<CisSearchResult>.ErrorResponse(
                    $"No webloan records found for CIS number '{cisNo}'."))
                : Results.Ok(ApiResponse<CisSearchResult>.SuccessResponse(
                    result, "CIS search completed successfully."));
        })
        .WithName("SearchCis")
        .Produces<ApiResponse<CisSearchResult>>(200)
        .Produces<ApiResponse<CisSearchResult>>(404)
        .WithSummary("Step 1: Search CIS - returns borrower info + account list for selection");

        // ─── Step 2: Get Account with PNs ──────────────────────────────────
        // GET /api/webloans/cis/{cisNo}/accounts/{accountNo}
        // Returns all PN records for the selected account.
        group.MapGet("/cis/{cisNo}/accounts/{accountNo}", async (
            string cisNo,
            string accountNo,
            IWebLoanService webLoanService) =>
        {
            var result = await webLoanService.GetAccountWithPnsAsync(cisNo, accountNo);

            return result is null
                ? Results.NotFound(ApiResponse<AccountWithPnsResponse>.ErrorResponse(
                    $"Account '{accountNo}' not found for CIS '{cisNo}'."))
                : Results.Ok(ApiResponse<AccountWithPnsResponse>.SuccessResponse(
                    result, "Account PN records retrieved successfully."));
        })
        .WithName("GetAccountWithPns")
        .Produces<ApiResponse<AccountWithPnsResponse>>(200)
        .Produces<ApiResponse<AccountWithPnsResponse>>(404)
        .WithSummary("Step 2: Get account detail with all PN records for selected account");

        // ─── Original full profile (backward compatibility) ────────────────
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
        .Produces<ApiResponse<WebLoanBorrowerResponse>>(404)
        .WithSummary("Full borrower profile (backward compatible)");
    }
}

using EBI.ALAS.Api.Common.Models;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;

namespace EBI.ALAS.Api.Features.WebLoans;
public static class WebLoanEndpoints
{
    public static void MapWebLoanEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/webloans")
            .WithTags("WebLoans")
            .RequireAuthorization();

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

        // ─── Paginated PN history per account ──────────────────────────────
        // GET /api/webloans/cis/{cisNo}/accounts/{accountNo}/promissory-notes?page=&pageSize=
        // Returns a single page of PN records for the (CIS, account) pair.
        // page >= 1, 1 <= pageSize <= 100. IDOR protected — 404 when the
        // account does not belong to the given CIS.
        group.MapGet("/cis/{cisNo}/accounts/{accountNo}/promissory-notes", async (
            string cisNo,
            string accountNo,
            IWebLoanService webLoanService,
            IValidator<PaginationRequest> paginationValidator,
            int page = 1,
            int pageSize = PaginationRequest.DefaultPageSize) =>
        {
            var pagination = new PaginationRequest(page, pageSize);

            // FluentValidation auto-validation already runs on [AsParameters]
            // model binding, but for query-string primitives we run it
            // explicitly so the 400 contract is honoured.
            var validation = await paginationValidator.ValidateAsync(pagination);
            if (!validation.IsValid)
            {
                var errors = validation.Errors.Select(e => e.ErrorMessage).ToList();
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    "Invalid pagination parameters.", errors));
            }

            var result = await webLoanService.GetAccountPromissoryNotesPagedAsync(
                cisNo, accountNo, pagination);

            return result is null
                ? Results.NotFound(ApiResponse<PagedResponse<PnRecord>>.ErrorResponse(
                    $"Account '{accountNo}' not found for CIS '{cisNo}'."))
                : Results.Ok(ApiResponse<PagedResponse<PnRecord>>.SuccessResponse(
                    result, "Account promissory notes retrieved successfully."));
        })
        .WithName("GetAccountPromissoryNotesPaged")
        .Produces<ApiResponse<PagedResponse<PnRecord>>>(200)
        .Produces<ApiResponse<PagedResponse<PnRecord>>>(400)
        .Produces<ApiResponse<PagedResponse<PnRecord>>>(404)
        .WithSummary("Get a paginated slice of PN records for an account (full history)");

        // ─── Active Loans by Account ───────────────────────────────────────
        // GET /api/webloans/cis/{cisNo}/accounts/{accountNo}/active-loans
        // Returns all active loans matching: acct_no + bch='000' + is_loan=1 + loan_status != 10,
        // ordered by date_granted desc. Returns 404 if the account does not
        // belong to the given CIS (prevents cross-tenant enumeration).
        group.MapGet("/cis/{cisNo}/accounts/{accountNo}/active-loans", async (
            string cisNo,
            string accountNo,
            IWebLoanService webLoanService) =>
        {
            var result = await webLoanService.GetActiveLoansByAccountAsync(cisNo, accountNo);

            return result is null
                ? Results.NotFound(ApiResponse<ActiveLoansResponse>.ErrorResponse(
                    $"Account '{accountNo}' not found for CIS '{cisNo}'."))
                : Results.Ok(ApiResponse<ActiveLoansResponse>.SuccessResponse(
                    result, "Active loans retrieved successfully."));
        })
        .WithName("GetActiveLoansByAccount")
        .Produces<ApiResponse<ActiveLoansResponse>>(200)
        .Produces<ApiResponse<ActiveLoansResponse>>(404)
        .WithSummary("Get all active loans for a (CIS, account) pair");

        // ─── Original full profile (backward compatibility) ────────────────
        // GET /api/webloans/cis/{cisNo}
        // Returns all webloan data for a borrower, structured per ALAS
        // application sections (personal info, loan info, outstanding loans, reloans).
        // Existing endpoint — preserved as-is for callers that have not opted
        // into the new paginated shape. Note: this response is unbounded and
        // can be very large for corporate borrowers; prefer the paginated
        // variant below for any new integration.
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

        // Bounded JSON payload — corporate borrowers with many accounts no longer
        // return multi-megabyte responses. Each account carries at most
        // Constants.RecentPnPerAccount recent PNs; use the dedicated
        // /promissory-notes endpoint for arbitrary per-account PN history.
        group.MapGet("/cis/{cisNo}/paginated", async (
            string cisNo,
            IWebLoanService webLoanService,
            IValidator<PaginationRequest> paginationValidator,
            int page = 1,
            int pageSize = PaginationRequest.DefaultPageSize) =>
        {
            var pagination = new PaginationRequest(page, pageSize);

            var validation = await paginationValidator.ValidateAsync(pagination);
            if (!validation.IsValid)
            {
                var errors = validation.Errors.Select(e => e.ErrorMessage).ToList();
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    "Invalid pagination parameters.", errors));
            }

            var result = await webLoanService.GetBorrowerByCisPagedAsync(cisNo, pagination);

            return result is null
                ? Results.NotFound(ApiResponse<PagedResponse<AccountWithPnsPagedItem>>.ErrorResponse(
                    $"No webloan records found for CIS number '{cisNo}'."))
                : Results.Ok(ApiResponse<PagedResponse<AccountWithPnsPagedItem>>.SuccessResponse(
                    result, "Paginated borrower profile retrieved successfully."));
        })
        .WithName("GetWebLoanByCisPaged")
        .Produces<ApiResponse<PagedResponse<AccountWithPnsPagedItem>>>(200)
        .Produces<ApiResponse>(400)
        .Produces<ApiResponse<PagedResponse<AccountWithPnsPagedItem>>>(404)
        .WithSummary("Bounded paginated borrower profile — accounts list + per-account recent PNs");
    }
}

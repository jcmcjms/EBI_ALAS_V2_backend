using System.Security.Claims;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Features.WebLoans;

public static class WebLoanEndpoints
{
    public static void MapWebLoanEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/webloans")
            .WithTags("WebLoans")
            .RequireAuthorization("CanViewLoan");

        // ─── Step 1: CIS search ────────────────────────────────────────
        // Returns the borrower profile + flat list of accounts. The bch
        // is taken from the JWT — never from the client — so a user
        // cannot spoof another branch by adding it to the query string.
        // Admin role passes null bch (no filter, since CIS search is not
        // branch-scoped anyway).
        group.MapGet("/cis/{cisNo}/search", async (
            string cisNo,
            ClaimsPrincipal user,
            IWebLoanService webLoanService,
            CancellationToken ct) =>
        {
            var bch = ResolveBranchForUser(user);

            var result = await webLoanService.SearchByCisAsync(cisNo, bch, ct);
            return result is null
                ? Results.NotFound(ApiResponse.ErrorResponse("CIS not found"))
                : Results.Ok(ApiResponse<CisSearchResponse>.SuccessResponse(result));
        })
        .WithName("SearchCis")
        .Produces<ApiResponse<CisSearchResponse>>(200)
        .Produces<ApiResponse>(404)
        .Produces<ApiResponse>(401);

        // ─── Step 2: outstanding loans for an account ──────────────────
        // The route parameter `accountId` is the combined
        // "<branchCode>-<accountNo>" form (e.g. "011-05-13081-1"). The
        // branch is therefore caller-controlled — the JWT no longer
        // restricts branch scope for this endpoint, mirroring the
        // original SQL's WHERE bch = ... AND acct_no = ... shape.
        // Admin and non-Admin behave identically for the branch filter;
        // the JWT still gates *which* accounts a caller may read via
        // AccountBelongsToCisAsync (the (bch, acct_no, cis_no) ownership
        // check).
        group.MapGet("/cis/{cisNo}/accounts/{accountId}/outstanding-loans", async (
            string cisNo,
            string accountId,
            IWebLoanService webLoanService,
            CancellationToken ct) =>
        {
            var result = await webLoanService.GetOutstandingLoansAsync(cisNo, accountId, ct);
            return result is null
                ? Results.NotFound(ApiResponse.ErrorResponse("Account not found for the given CIS"))
                : Results.Ok(ApiResponse<OutstandingLoansResponse>.SuccessResponse(result));
        })
        .WithName("GetOutstandingLoans")
        .Produces<ApiResponse<OutstandingLoansResponse>>(200)
        .Produces<ApiResponse>(404)
        .Produces<ApiResponse>(401);

        // ─── Step 3: pending loan for an account ──────────────────────
        // Same combined-`accountId` shape as the outstanding-loans
        // endpoint. Returns the in-flight pre_loan_data rows + NTHP
        // enrichment. Anti-enumeration guard runs first (mirrors Step 2).
        //
        // 200 with Loans=[] is a valid response: the (cisNo, accountId)
        // pair exists but has no pending loan. Only 404 when the
        // account↔CIS pair is unknown.
        group.MapGet("/cis/{cisNo}/accounts/{accountId}/pending-loan", async (
            string cisNo,
            string accountId,
            IWebLoanService webLoanService,
            CancellationToken ct) =>
        {
            var result = await webLoanService.GetPendingLoanAsync(cisNo, accountId, ct);
            return result is null
                ? Results.NotFound(ApiResponse.ErrorResponse("Account not found for the given CIS"))
                : Results.Ok(ApiResponse<PendingLoanResponse>.SuccessResponse(result));
        })
        .WithName("GetPendingLoan")
        .Produces<ApiResponse<PendingLoanResponse>>(200)
        .Produces<ApiResponse>(404)
        .Produces<ApiResponse>(401);
    }

    /// <summary>
    /// Resolves the authenticated user's webloan <c>bch</c> from the JWT
    /// <c>branchId</c> claim, with an Admin-role bypass.
    ///
    /// ALAS <c>Branch.Code</c> and webloan <c>bch</c> are the same string
    /// (e.g. <c>"011"</c>), so direct mapping.
    ///
    /// Returns:
    ///   * <c>null</c> when the caller has the Admin role — branch
    ///     scoping is bypassed, the repository emits an "IS NULL OR"
    ///     predicate. Mirrors the existing HasPermission Admin wildcard
    ///     in ClaimsPrincipalExtensions.
    ///   * <c>string</c> — the user's <c>branchId</c> claim value, when
    ///     the caller is non-Admin.
    ///
    /// Throws <see cref="UnauthorizedAccessException"/> when a non-Admin
    /// token lacks the <c>branchId</c> claim. Every token this service
    /// issues carries one, so a missing claim means a malformed or
    /// external token; the existing GlobalExceptionHandler surfaces this
    /// as 401.
    /// </summary>
    private static string? ResolveBranchForUser(ClaimsPrincipal user)
    {
        if (user.IsInRole("Admin"))
        {
            return null;
        }

        var raw = user.GetBranchId();
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new UnauthorizedAccessException(
                "JWT is missing the branchId claim. Re-authenticate.");
        }
        return raw;
    }
}
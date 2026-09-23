using System.Security.Claims;
using EBI.ALAS.Api.Common.Authorization;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using Microsoft.Extensions.Caching.Memory;

namespace EBI.ALAS.Api.Features.WebLoans;

/// <summary>Wrapper so "not found" is cacheable — TryGetValue cannot store null.</summary>
public sealed record LoanClassCacheEntry(CatLoanClassResponse? Value);

public static class WebLoanEndpoints
{
    public static void MapWebLoanEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/webloans")
            .WithTags("WebLoans")
            .RequireAuthorization("CanViewLoan");

        // Step 1: CIS search
        // Returns the borrower profile + flat list of accounts. The bch
        // is taken from the JWT — never from the client — so a user
        // cannot spoof another branch by adding it to the query string.
        // Non-Admin callers (e.g. Encoder) are scoped to their branch
        // so they only see accounts belonging to their branch. Admin
        // role passes null bch (no filter — sees all branches).
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

        // Step 2: outstanding loans for an account
        // The route parameter `accountId` is the combined
        // "<branchCode>-<accountNo>" form (e.g. "011-05-13081-1").
        // Branch scope check runs FIRST via IBranchScopeService — an
        // explicit 403 when the account's branch is outside the caller's
        // scope, never a silent-empty 200. Anti-enumeration guard
        // (AccountBelongsToCisAsync) runs second.
        group.MapGet("/cis/{cisNo}/accounts/{accountId}/outstanding-loans", async (
            string cisNo,
            string accountId,
            IWebLoanService webLoanService,
            IBranchScopeService branchScope,
            ClaimsPrincipal user,
            int pageSize = 50,
            int pageNumber = 1,
            CancellationToken ct = default) =>
        {
            var (branchCode, _) = WebLoanAccountId.Parse(accountId);

            // Scope check FIRST — can never return "empty set";
            // BranchScopeService falls back to home branch and throws
            // on misconfiguration.
            if (!await branchScope.CanAccessBranchAsync(user, branchCode, ct))
                return Results.Json(
                    ApiResponse.ErrorResponse(
                        $"Account branch {branchCode} is outside your branch scope."),
                    statusCode: StatusCodes.Status403Forbidden);

            // Clamp to a sane ceiling: 500 rows max per page keeps the
            // response payload under 1MB even with the LEFT JOINs to
            // amort_data and loan_product. Anything bigger is a UI bug
            // — humans don't scroll through 500 outstanding loans.
            pageSize = Math.Clamp(pageSize, 1, 500);
            pageNumber = Math.Max(1, pageNumber);

            var result = await webLoanService.GetOutstandingLoansAsync(cisNo, accountId, pageSize, pageNumber, ct);
            return result is null
                ? Results.NotFound(ApiResponse.ErrorResponse("Account not found for the given CIS"))
                : Results.Ok(ApiResponse<OutstandingLoansResponse>.SuccessResponse(result));
        })
        .WithName("GetOutstandingLoans")
        .Produces<ApiResponse<OutstandingLoansResponse>>(200)
        .Produces<ApiResponse>(404)
        .Produces<ApiResponse>(401);

        // Step 3: pending loan for an account
        // Same combined-`accountId` shape as the outstanding-loans
        // endpoint. Returns the in-flight pre_loan_data rows + NTHP
        // enrichment. Branch scope check runs FIRST via
        // IBranchScopeService — explicit 403 instead of silent-empty.
        // Anti-enumeration guard runs second.
        // 200 with Loans=[] is a valid response: the (cisNo, accountId)
        // pair exists but has no pending loan. Only 404 when the
        // account↔CIS pair is unknown. Only 403 when outside scope.
        group.MapGet("/cis/{cisNo}/accounts/{accountId}/pending-loan", async (
            string cisNo,
            string accountId,
            IWebLoanService webLoanService,
            IBranchScopeService branchScope,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var (branchCode, _) = WebLoanAccountId.Parse(accountId);

            // Scope check FIRST — can never return "empty set".
            if (!await branchScope.CanAccessBranchAsync(user, branchCode, ct))
                return Results.Json(
                    ApiResponse.ErrorResponse(
                        $"Account branch {branchCode} is outside your branch scope."),
                    statusCode: StatusCodes.Status403Forbidden);

            var result = await webLoanService.GetPendingLoanAsync(cisNo, accountId, ct);
            return result is null
                ? Results.NotFound(ApiResponse.ErrorResponse("Account not found for the given CIS"))
                : Results.Ok(ApiResponse<PendingLoanResponse>.SuccessResponse(result));
        })
        .WithName("GetPendingLoan")
        .Produces<ApiResponse<PendingLoanResponse>>(200)
        .Produces<ApiResponse>(404)
        .Produces<ApiResponse>(401);

        // Step 4: active loan products lookup
        // Surfaces every row in dbo.loan_product where expiration IS NULL
        // — i.e. products that have not been retired by the webloan
        // system. Projects only id_code + description. Intended as a
        // dropdown source for the loan-origination UI.
        // Gated by the same `CanViewLoan` policy as the rest of the
        // group — loan origination / review workflows need the same
        // read-only product access, and reusing the policy avoids
        // creating a parallel permission tier for a 2-column lookup.
        // 200 with `data: []` is a valid response (e.g. during a webloan
        // cutover when no products are flagged active). The lookup is
        // global — no per-branch / per-CIS scoping — because products
        // are reference data shared across all branches.
        group.MapGet("/loan-products", async (
            IWebLoanService webLoanService,
            CancellationToken ct) =>
        {
            var products = await webLoanService.GetActiveLoanProductsAsync(ct);
            return Results.Ok(ApiResponse<IReadOnlyList<LoanProductDto>>.SuccessResponse(products));
        })
        .WithName("GetActiveLoanProducts")
        .Produces<ApiResponse<IReadOnlyList<LoanProductDto>>>(200)
        .Produces<ApiResponse>(401);

        // Returns the `cat_loan_class` value from dbo.loan_data for a
        // single (bch, loan_no, loan_product) composite key.
        // All three parameters are caller-supplied via query string.
        // No JWT-derived branch fallback — the URL is the identity.
        // Gated by the same `CanViewLoan` policy as the rest of the
        // group (read-only webloan access), consistent with how
        // `/loan-products` was added without a new policy tier.
        // 200 with null CatLoanClass: the row exists but
        // dbo.loan_data.cat_loan_class IS NULL — UI renders a placeholder.
        // 404: no row found for the (bch, loan_no, loan_product) triple.
        group.MapGet("/loan-class", async (
            string bch,
            string loanNo,
            string loanProduct,
            IWebLoanService webLoanService,
            IMemoryCache cache,
            CancellationToken ct) =>
        {
            // All three are required — reject the request early rather
            // than letting the null propagate to SQL where it would
            // return unintended rows.
            if (string.IsNullOrWhiteSpace(bch)
                || string.IsNullOrWhiteSpace(loanNo)
                || string.IsNullOrWhiteSpace(loanProduct))
            {
                return Results.BadRequest(
                    ApiResponse.ErrorResponse(
                        "bch, loanNo, and loanProduct are all required."));
            }

            // Static reference data: 12h positive TTL, 5min negative TTL so a
            // mis-keyed lookup self-heals quickly without re-hitting the legacy DB
            // on every page load. Keeps the review page's render path off the
            // legacy core entirely once warm.
            var cacheKey = $"webloan:loan-class:{bch}:{loanNo}:{loanProduct}";
            if (cache.TryGetValue(cacheKey, out LoanClassCacheEntry? entry) && entry is not null)
            {
                return entry.Value is null
                    ? Results.NotFound(ApiResponse.ErrorResponse(
                        "Loan not found in webloan for the given (bch, loan_no, loan_product)."))
                    : Results.Ok(ApiResponse<CatLoanClassResponse>.SuccessResponse(entry.Value));
            }

            var result = await webLoanService.GetCatLoanClassAsync(bch, loanNo, loanProduct, ct);

            cache.Set(cacheKey, new LoanClassCacheEntry(result), new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = result is null
                    ? TimeSpan.FromMinutes(5)
                    : TimeSpan.FromHours(12),
                Size = 1,
            });

            return result is null
                ? Results.NotFound(ApiResponse.ErrorResponse(
                    "Loan not found in webloan for the given (bch, loan_no, loan_product)."))
                : Results.Ok(ApiResponse<CatLoanClassResponse>.SuccessResponse(result));
        })
        .WithName("GetLoanClass")
        .Produces<ApiResponse<CatLoanClassResponse>>(200)
        .Produces<ApiResponse>(400)
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
    ///     scoping is bypassed, the repository returns all branches.
    ///   * <c>string</c> — the user's <c>branchId</c> claim value, when
    ///     the caller is non-Admin. The service scopes account results
    ///     to this branch so encoders only see their own branch.
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
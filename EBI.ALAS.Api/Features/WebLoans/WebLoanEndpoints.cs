using System.Security.Claims;
using EBI.ALAS.Api.Shared.Authorization;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using Microsoft.Extensions.Caching.Memory;
namespace EBI.ALAS.Api.Features.WebLoans;
public sealed record LoanClassCacheEntry(CatLoanClassResponse? Value);
public static class WebLoanEndpoints
{
    public static void MapWebLoanEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/webloans")
            .WithTags("WebLoans")
            .RequireAuthorization("CanViewLoan");
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
            if (!await branchScope.CanAccessBranchAsync(user, branchCode, ct))
                return Results.Json(
                    ApiResponse.ErrorResponse(
                        $"Account branch {branchCode} is outside your branch scope."),
                    statusCode: StatusCodes.Status403Forbidden);
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
        group.MapGet("/cis/{cisNo}/accounts/{accountId}/pending-loan", async (
            string cisNo,
            string accountId,
            IWebLoanService webLoanService,
            IBranchScopeService branchScope,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var (branchCode, _) = WebLoanAccountId.Parse(accountId);
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
        group.MapGet("/cis/{cisNo}/cocree-status", async (
            string cisNo,
            IWebLoanService webLoanService,
            CancellationToken ct) =>
        {
            var result = await webLoanService.GetCocreeStatusAsync(cisNo, ct);
            return Results.Ok(ApiResponse<CocreeStatusResponse>.SuccessResponse(result));
        })
        .WithName("GetCocreeStatus")
        .Produces<ApiResponse<CocreeStatusResponse>>(200)
        .Produces<ApiResponse>(401);
        group.MapGet("/loan-class", async (
            string bch,
            string loanNo,
            string loanProduct,
            IWebLoanService webLoanService,
            IMemoryCache cache,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(bch)
                || string.IsNullOrWhiteSpace(loanNo)
                || string.IsNullOrWhiteSpace(loanProduct))
            {
                return Results.BadRequest(
                    ApiResponse.ErrorResponse(
                        "bch, loanNo, and loanProduct are all required."));
            }
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

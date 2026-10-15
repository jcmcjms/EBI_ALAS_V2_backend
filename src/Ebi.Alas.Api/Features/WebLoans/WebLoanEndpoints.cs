using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;

namespace Ebi.Alas.Api.Features.WebLoans;

public static class WebLoanEndpoints
{
    public static void MapWebLoans(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/webloans").WithTags("WebLoans").RequireAuthorization("CanViewWebLoans");

        group.MapGet("/cis", async (
            string q,
            int? max,
            IWebLoanReader reader,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Results.Problem(title: "Bad Request", detail: "Query parameter 'q' is required.", statusCode: 400);
            }

            var items = await reader.SearchCisAsync(q, max ?? 20, cancellationToken);
            return Results.Ok(items);
        });

        group.MapGet("/cis/{cisNo}/search", async (
            string cisNo,
            IWebLoanReader reader,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var detail = await reader.GetCisDetailAsync(cisNo, cancellationToken);
            if (detail is null)
            {
                return Results.NotFound(new { title = "Not Found", detail = $"CIS '{cisNo}' was not found." });
            }

            // Encoders must not see LAI accounts belonging to other branches.
            var scoped = WebLoanAccess.FilterAccountsForCaller(detail, caller);
            return Results.Ok(scoped);
        });

        group.MapGet("/loan-products", async (
            int? max,
            IWebLoanReader reader,
            CancellationToken cancellationToken) =>
        {
            var items = await reader.GetActiveProductsAsync(max ?? 50, cancellationToken);
            return Results.Ok(items);
        });

        group.MapGet("/cis/{cisNo}/accounts/{accountId}/outstanding-loans", async (
            string cisNo,
            string accountId,
            int? pageSize,
            int? pageNumber,
            IWebLoanReader reader,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var caller = CallerContext.Require(user);
                var (accountBranch, _) = WebLoanAccountId.Parse(accountId);
                if (!WebLoanAccess.CanUseAccount(caller, accountBranch))
                {
                    throw new NotFoundException("Account", accountId);
                }

                var result = await reader.GetAccountOutstandingLoansAsync(
                    cisNo, accountId, pageSize ?? 50, pageNumber ?? 1, cancellationToken);
                return result is null
                    ? Results.NotFound(new { title = "Not Found", detail = "Account not found for the given CIS." })
                    : Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(title: "Bad Request", detail: ex.Message, statusCode: 400);
            }
        });

        group.MapGet("/cis/{cisNo}/accounts/{accountId}/pending-loan", async (
            string cisNo,
            string accountId,
            IWebLoanReader reader,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var caller = CallerContext.Require(user);
                var (accountBranch, _) = WebLoanAccountId.Parse(accountId);
                if (!WebLoanAccess.CanUseAccount(caller, accountBranch))
                {
                    throw new NotFoundException("Account", accountId);
                }

                var result = await reader.GetAccountPendingLoanAsync(cisNo, accountId, cancellationToken);
                return result is null
                    ? Results.NotFound(new { title = "Not Found", detail = "Account not found for the given CIS." })
                    : Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(title: "Bad Request", detail: ex.Message, statusCode: 400);
            }
        });

        group.MapGet("/outstanding/{cifNo}", async (
            string cifNo,
            int? max,
            IWebLoanReader reader,
            CancellationToken cancellationToken) =>
        {
            var items = await reader.GetOutstandingAsync(cifNo, max ?? 20, cancellationToken);
            return Results.Ok(items);
        });

        group.MapGet("/loan-class", async (
            string bch,
            string loanNo,
            string loanProduct,
            IWebLoanReader reader,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(bch)
                || string.IsNullOrWhiteSpace(loanNo)
                || string.IsNullOrWhiteSpace(loanProduct))
            {
                return Results.Problem(
                    title: "Bad Request",
                    detail: "bch, loanNo, and loanProduct are all required.",
                    statusCode: 400);
            }

            var catLoanClass = await reader.GetCatLoanClassAsync(bch, loanNo, loanProduct, cancellationToken);
            return Results.Ok(new CatLoanClassResponse(bch, loanNo, loanProduct, catLoanClass));
        })
        .WithName("GetLoanClass");
    }
}

public sealed record CatLoanClassResponse(
    string Bch,
    string LoanNo,
    string LoanProduct,
    string? CatLoanClass);

using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.GetLoan;
using Ebi.Alas.Api.Features.LoanApplications.ListLoans;
using Ebi.Alas.Api.Features.LoanApplications.Submit;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanComputation;
using Ebi.Alas.Api.Features.Pagination;

namespace Ebi.Alas.Api.Features.LoanApplications;

public static class LoanEndpoints
{
    public static void MapLoans(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/loans").WithTags("Loans").RequireAuthorization();

        group.MapPost("/", async (
            SubmitLoanRequest request,
            SubmitLoanHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var result = await handler.HandleAsync(request, caller, cancellationToken);
            return Results.Created($"/api/loans/{result.Id}", result);
        });

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            LoanStatus? status,
            string? branchId,
            ListLoansHandler handler,
            ClaimsPrincipal user,
            Microsoft.Extensions.Options.IOptions<Composition.ApiOptions> options,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var opt = options.Value;
            var result = await handler.HandleAsync(
                new PageRequest { Page = page ?? 1, PageSize = pageSize ?? opt.DefaultPageSize },
                opt.MaxPageSize,
                opt.DefaultPageSize,
                status,
                branchId,
                caller,
                cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/{id:guid}", async (Guid id, GetLoanHandler handler, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var result = await handler.HandleAsync(id, caller, cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/lam/{lamId}", async (string lamId, GetLoanByLamIdHandler handler, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var result = await handler.HandleAsync(lamId, caller, cancellationToken);
            return Results.Ok(result);
        });

        group.MapPost("/compute", (LoanComputationInput input, LoanComputationService service) =>
        {
            var result = service.Compute(input);
            return Results.Ok(result);
        });
    }
}

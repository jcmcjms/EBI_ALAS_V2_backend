using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.Revise;

namespace Ebi.Alas.Api.Features.LoanApplications;

public static class ReviseLoanEndpoint
{
    public static void MapReviseLoan(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/loans").WithTags("Loans").RequireAuthorization();

        group.MapPut("/{id:guid}/revise", async (
            Guid id,
            ReviseLoanCommand command,
            ReviseLoanHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var result = await handler.HandleAsync(id, command, caller, cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization("CanReviseLoan")
        .RequireRateLimiting("write")
        .WithName("ReviseLoan");
    }
}

using Microsoft.AspNetCore.Authorization;

namespace Ebi.Alas.Api.Features.LoanProducts.DocumentRequirements;

public static class DocumentRequirementEndpoints
{
    public static void MapDocumentRequirementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/loan-products/{code}/document-requirements")
            .WithTags("LoanProducts");

        group.MapGet("/", async (
            string code,
            GetDocumentRequirementsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var items = await handler.HandleAsync(code, cancellationToken);
            return Results.Ok(items);
        })
        .RequireAuthorization();

        group.MapPut("/", async (
            string code,
            ReplaceDocumentRequirementsRequest request,
            ReplaceDocumentRequirementsHandler handler,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var items = await handler.HandleAsync(code, request, cancellationToken);
                return Results.Ok(items);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: ex.Message);
            }
        })
        .RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" })
        .RequireRateLimiting("write");
    }
}

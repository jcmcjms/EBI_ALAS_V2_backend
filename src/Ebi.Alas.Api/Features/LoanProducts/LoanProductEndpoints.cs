using Ebi.Alas.Api.Composition.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanProducts;

public static class LoanProductEndpoints
{
    public static void MapLoanProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/loan-products").WithTags("LoanProducts");

        group.MapGet("/", async (
            Infrastructure.Persistence.AlasDbContext db,
            CancellationToken cancellationToken) =>
        {
            var products = await db.LoanProducts
                .AsNoTracking()
                .OrderBy(p => p.Name)
                .ToListAsync(cancellationToken);
            return Results.Ok(products.Select(p => p.ToResponse()).ToList());
        })
        .RequireAuthorization();

        group.MapPost("/sync", async (
            SyncLoanProductsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" })
        .RequireRateLimiting("write");

        group.MapGet("/export", async (
            bool? includeRetired,
            Infrastructure.Persistence.AlasDbContext db,
            CancellationToken cancellationToken) =>
        {
            var query = db.LoanProducts.AsNoTracking();
            if (includeRetired == false)
            {
                query = query.Where(p => p.IsActive);
            }

            var products = await query.OrderBy(p => p.Code).ToListAsync(cancellationToken);
            var bytes = LoanProductExcel.BuildExport(products.Select(p => p.ToResponse()).ToList());
            return Results.File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"loan-products-{DateTime.UtcNow:yyyyMMdd}.xlsx");
        })
        .RequireAuthorization();

        group.MapGet("/import/template", () =>
            Results.File(
                LoanProductExcel.BuildTemplate(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "loan-product-import-template.xlsx"))
            .RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" });

        group.MapPost("/import", async (
            HttpRequest request,
            ImportLoanProductsHandler handler,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status415UnsupportedMediaType,
                    title: "Unsupported Media Type",
                    detail: "Upload the spreadsheet as multipart/form-data with a 'file' field.");
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: "A non-empty 'file' upload is required.");
            }

            if (file.Length > 10 * 1024 * 1024)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    title: "Payload Too Large",
                    detail: "Import file must be at most 10 MB.");
            }

            if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: "Only .xlsx files are supported.");
            }

            await using var stream = file.OpenReadStream();
            var result = await handler.ImportAsync(stream, cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" })
        .RequireRateLimiting("write");

        group.MapGet("/{code}", async (
            string code,
            Infrastructure.Persistence.AlasDbContext db,
            CancellationToken cancellationToken) =>
        {
            var product = await db.LoanProducts
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Code == code, cancellationToken);
            if (product is null)
            {
                throw new NotFoundException("Loan product", code);
            }

            return Results.Ok(product.ToResponse());
        })
        .RequireAuthorization();

        group.MapPut("/{code}", async (
            string code,
            UpdateLoanProductPolicyRequest request,
            UpdateLoanProductPolicyHandler handler,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await handler.HandleAsync(code, request, cancellationToken);
                return Results.Ok(updated);
            }
            catch (ArgumentOutOfRangeException ex)
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

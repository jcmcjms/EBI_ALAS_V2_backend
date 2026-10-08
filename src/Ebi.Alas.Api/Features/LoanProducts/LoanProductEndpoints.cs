using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanProducts;

public static class LoanProductEndpoints
{
    public static void MapLoanProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/loan-products", async (
            Infrastructure.Persistence.AlasDbContext db,
            CancellationToken cancellationToken) =>
        {
            var items = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                db.LoanProducts.AsNoTracking().OrderBy(p => p.Name)
                    .Select(p => new { p.Code, p.Name, p.InterestRatePerMonth, p.MinTermDays, p.MaxTermDays, p.IsActive }),
                cancellationToken);
            return Results.Ok(items);
        })
        .RequireAuthorization()
        .WithTags("LoanProducts");

        endpoints.MapPost("/api/loan-products/import", async (
            HttpRequest request,
            Infrastructure.Persistence.AlasDbContext db,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            const int maxImportRows = 1000;
            using var reader = new StreamReader(request.Body);
            var csv = await reader.ReadToEndAsync(cancellationToken);
            var rows = LoanProductCsv.Parse(csv);
            if (rows.Count > maxImportRows)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    title: "Too many rows",
                    detail: $"Import is limited to {maxImportRows} rows.");
            }

            foreach (var row in rows)
            {
                if (!await db.LoanProducts.AnyAsync(p => p.Code == row.Code, cancellationToken))
                {
                    db.LoanProducts.Add(LoanProduct.Create(
                        row.Code, row.Name, row.Rate, row.MinTerm, row.MaxTerm, timeProvider.GetUtcNow()));
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new ImportLoanProductsResponse(rows.Count));
        })
        .RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" })
        .RequireRateLimiting("write")
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(1_048_576))
        .WithTags("LoanProducts");
    }
}

public sealed record ImportLoanProductsResponse(int Imported);

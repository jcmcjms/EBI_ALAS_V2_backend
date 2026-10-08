namespace Ebi.Alas.Api.Features.WebLoans;

public static class WebLoanEndpoints
{
    public static void MapWebLoans(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/webloans").WithTags("WebLoans").RequireAuthorization();

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

        group.MapGet("/loan-products", async (
            int? max,
            IWebLoanReader reader,
            CancellationToken cancellationToken) =>
        {
            var items = await reader.GetActiveProductsAsync(max ?? 50, cancellationToken);
            return Results.Ok(items);
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
    }
}

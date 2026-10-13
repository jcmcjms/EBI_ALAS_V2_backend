namespace Ebi.Alas.Api.Features.Deviations;

/// <summary>
/// Stable deviation catalog (None=0, Minor=1, Major=2) matching the prior system seed.
/// </summary>
public static class DeviationCatalog
{
    public sealed record Item(int Id, string Description, int Severity);

    public static readonly IReadOnlyList<Item> All =
    [
        new(1, "Age not within the prescribed parameters", 2),
        new(2, "Discounted Application Fee", 2),
        new(3, "Interest rate reduction", 2),
        new(4, "With past due account - non performing loan", 2),
        new(5, "Lacking bank statement of account", 1),
        new(6, "Lacking CIBI", 1),
        new(7, "Lacking marriage cert. with surname as single", 1),
        new(8, "Lacking one or two payslip(s) for new atm loan", 1),
        new(9, "Lacking signature in application form", 1),
        new(10, "Lacking SPAs to claim ATM", 1),
        new(11, "No appointment record and/or service record", 1),
        new(12, "No FI SOA and loan ledger", 1),
        new(13, "No latest payslip", 1),
        new(14, "No interview sheet", 1),
        new(15, "No orientation form or old form submitted", 1),
        new(16, "No valid identification cards", 1),
        new(17, "Total consumer loan exposure exceeding 1.2 million", 1),
        new(18, "With blocked ATIM in same school", 1),
        new(19, "With history of delinquency in the latest loan availment", 1),
        new(20, "With NFIS findings", 1),
        new(21, "With past due account - performing", 1),
    ];
}

public sealed record DeviationCatalogItemDto(int Id, string Description, int Severity);

public static class DeviationCatalogEndpoints
{
    public static void MapDeviationCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/deviation-catalog", () =>
            Results.Ok(DeviationCatalog.All.Select(c =>
                new DeviationCatalogItemDto(c.Id, c.Description, c.Severity))))
        .RequireAuthorization()
        .WithName("GetDeviationCatalog")
        .WithTags("Deviations");
    }
}

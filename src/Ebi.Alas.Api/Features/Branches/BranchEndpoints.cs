namespace Ebi.Alas.Api.Features.Branches;

public sealed record BranchResponse(string Code, string Name);

public static class BranchCatalog
{
    public static readonly IReadOnlyList<BranchResponse> All =
    [
        new("001", "Head Office"),
        new("011", "Main Branch"),
        new("012", "North Branch"),
        new("013", "South Branch")
    ];
}

public static class BranchEndpoints
{
    public static void MapBranches(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/branches", () => Results.Ok(BranchCatalog.All))
            .RequireAuthorization()
            .WithName("GetBranches")
            .WithTags("Branches");

        endpoints.MapGet("/api/branches/{code}", (string code) =>
        {
            var branch = BranchCatalog.All.FirstOrDefault(b => b.Code == code);
            return branch is null ? Results.NotFound() : Results.Ok(branch);
        })
        .RequireAuthorization()
        .WithName("GetBranchByCode")
        .WithTags("Branches");
    }
}

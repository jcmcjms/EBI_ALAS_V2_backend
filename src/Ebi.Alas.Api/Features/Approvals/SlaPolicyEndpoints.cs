namespace Ebi.Alas.Api.Features.Approvals;

public static class SlaPolicyEndpoints
{
    public static void MapSlaPolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/loans/sla-policy", () =>
            Results.Ok(SlaPolicy.Default))
        .RequireAuthorization()
        .WithTags("Approvals");
    }
}

namespace Ebi.Alas.Api.Features.Roles;

public static class RoleEndpoints
{
    public static void MapRoles(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/roles", () => Results.Ok(RoleCatalog.All))
            .RequireAuthorization()
            .WithName("GetRoles")
            .WithTags("Roles");

        endpoints.MapGet("/api/roles/matrix", () => Results.Ok(RoleCatalog.All))
            .RequireAuthorization()
            .WithName("GetRoleMatrix")
            .WithTags("Roles");
    }
}

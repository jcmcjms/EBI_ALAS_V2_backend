using Alas.Api.Validation;
using Alas.Application.Admin.Roles;
using Alas.Application.Common.Security;
using Alas.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Alas.Api.Endpoints.Admin;

public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/roles")
            .RequireAuthorization(AlasPermissions.RolesManage)
            .WithTags("Role Management");

        group.MapGet("/", ListRolesAsync)
            .Produces<IReadOnlyCollection<RoleListItemDto>>();

        group.MapGet("/{id:guid}", GetRoleAsync)
            .Produces<RoleDetailDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateRoleAsync)
            .AddEndpointFilter<ValidationFilter<CreateRoleRequest>>()
            .Produces<RoleDetailDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPut("/{id:guid}/permissions", AssignPermissionsAsync)
            .AddEndpointFilter<ValidationFilter<AssignPermissionsRequest>>()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListRolesAsync(
        RoleService roleService,
        CancellationToken cancellationToken)
    {
        var roles = await roleService.ListAsync(cancellationToken);
        return Results.Ok(roles);
    }

    private static async Task<IResult> GetRoleAsync(
        Guid id,
        RoleService roleService,
        CancellationToken cancellationToken)
    {
        var role = await roleService.GetDetailAsync(id, cancellationToken);

        return role is null
            ? Results.NotFound()
            : Results.Ok(role);
    }

    private static async Task<IResult> CreateRoleAsync(
        CreateRoleRequest request,
        RoleService roleService,
        CancellationToken cancellationToken)
    {
        try
        {
            var role = await roleService.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/admin/roles/{role!.RoleId}", role);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> AssignPermissionsAsync(
        Guid id,
        AssignPermissionsRequest request,
        RoleService roleService,
        CancellationToken cancellationToken)
    {
        var success = await roleService.AssignPermissionsAsync(
            id, request.Permissions, cancellationToken);

        return success
            ? Results.NoContent()
            : Results.NotFound();
    }
}

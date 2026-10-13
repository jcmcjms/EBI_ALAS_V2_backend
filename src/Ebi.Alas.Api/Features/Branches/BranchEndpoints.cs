using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Branches;

public static class BranchEndpoints
{
    public static void MapBranches(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/branches").WithTags("Branches");

        group.MapGet("/", async (
            string? status,
            ListBranchesHandler handler,
            CancellationToken cancellationToken) =>
        {
            var items = await handler.HandleAsync(status, cancellationToken);
            return Results.Ok(items);
        })
        .RequireAuthorization()
        .WithName("GetBranches");

        group.MapGet("/{code}", async (
            string code,
            AlasDbContext db,
            CancellationToken cancellationToken) =>
        {
            var branch = await db.Branches.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Code == code, cancellationToken);
            if (branch is null)
            {
                throw new NotFoundException("Branch", code);
            }

            return Results.Ok(branch.ToResponse());
        })
        .RequireAuthorization()
        .WithName("GetBranchByCode");

        group.MapPost("/", async (
            CreateBranchRequest request,
            CreateBranchHandler handler,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await handler.HandleAsync(request, cancellationToken);
                return Results.Created($"/api/branches/{created.Code}", created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflict",
                    detail: ex.Message);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: ex.Message);
            }
        })
        .RequireAuthorization("CanManageBranches")
        .RequireRateLimiting("write");

        group.MapPut("/{code}", async (
            string code,
            UpdateBranchRequest request,
            UpdateBranchHandler handler,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await handler.HandleAsync(code, request, cancellationToken);
                return Results.Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: ex.Message);
            }
        })
        .RequireAuthorization("CanManageBranches")
        .RequireRateLimiting("write");
    }
}

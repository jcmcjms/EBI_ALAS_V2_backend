using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Revisions;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications;

public static class RevisionRequestEndpoints
{
    public static void MapRevisionRequestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/loans").WithTags("Revisions").RequireAuthorization();

        group.MapGet("/{id:guid}/revision-requests", async (
            Guid id,
            ClaimsPrincipal user,
            RevisionRequestService service,
            Infrastructure.Persistence.AlasDbContext db,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var accessible = await db.LoanApplications.AsNoTracking()
                .AnyAsync(l => l.Id == id && (caller.CanAccessAllBranches || l.BranchId == caller.BranchId),
                    cancellationToken);
            if (!accessible)
            {
                return Results.NotFound();
            }

            return Results.Ok(await service.ListAsync(id, cancellationToken));
        })
        .WithName("GetRevisionRequests");

        group.MapPut("/{id:guid}/revision-requests/{revisionId:guid}/resolve", async (
            Guid id,
            Guid revisionId,
            ClaimsPrincipal user,
            RevisionRequestService service,
            CancellationToken cancellationToken) =>
        {
            _ = CallerContext.Require(user);
            var ok = await service.ResolveAsync(id, revisionId, cancellationToken);
            return ok ? Results.NoContent() : Results.NotFound();
        })
        .RequireRateLimiting("write")
        .WithName("ResolveRevisionRequest");
    }
}

using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Workflow;

namespace Ebi.Alas.Api.Features.Workflow;

public static class WorkflowEndpoints
{
    public static void MapWorkflow(this IEndpointRouteBuilder endpoints)
    {
        var desk = endpoints.MapGroup("/api/loans/queue").WithTags("Workflow").RequireAuthorization();

        desk.MapGet("/my", async (
            DeskQueueService service,
            System.Security.Claims.ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            return Results.Ok(await service.GetMyDeskAsync(caller, cancellationToken));
        });

        desk.MapPost("/claim", async (
            DeskQueueService service,
            System.Security.Claims.ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            return Results.Ok(await service.ClaimHeadAsync(caller, cancellationToken));
        });

        desk.MapPost("/release", async (
            DeskQueueService service,
            System.Security.Claims.ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            await service.ReleaseClaimAsync(caller, cancellationToken);
            return Results.NoContent();
        });

        desk.MapPost("/{loanId:guid}/claim", async (
            Guid loanId,
            DeskQueueService service,
            System.Security.Claims.ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var result = await service.ClaimByIdAsync(loanId, caller, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        var group = endpoints.MapGroup("/api/workflow").WithTags("Workflow").RequireAuthorization();

        group.MapGet("/queue", async (
            WorkflowStage stage,
            string partitionKey,
            WorkflowQueueService queue,
            CancellationToken cancellationToken) =>
        {
            var items = await queue.ListAsync(stage, partitionKey, 50, cancellationToken);
            return Results.Ok(items.Select(i => new
            {
                i.Id,
                i.LoanApplicationId,
                Stage = i.Stage.ToString(),
                State = i.State.ToString(),
                i.PartitionKey,
                i.Sequence,
                i.OwnerUserId,
                i.LeasedAt
            }));
        });

        group.MapPost("/queue/claim", async (
            ClaimQueueRequest request,
            WorkflowQueueService queue,
            System.Security.Claims.ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var item = await queue.ClaimNextAsync(request.Stage, request.PartitionKey, caller.UserId, cancellationToken);
            return Results.Ok(new ClaimQueueResponse(
                item.Id,
                item.LoanApplicationId,
                item.Stage.ToString(),
                item.State.ToString(),
                item.LeasedAt));
        });

        group.MapPost("/queue/{id:guid}/release", async (
            Guid id,
            WorkflowQueueService queue,
            System.Security.Claims.ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            await queue.ReleaseAsync(id, caller.UserId, cancellationToken);
            return Results.NoContent();
        });
    }
}

public sealed record ClaimQueueRequest(WorkflowStage Stage, string PartitionKey);

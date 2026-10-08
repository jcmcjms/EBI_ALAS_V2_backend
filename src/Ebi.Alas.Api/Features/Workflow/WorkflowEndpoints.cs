using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Workflow;

namespace Ebi.Alas.Api.Features.Workflow;

public static class WorkflowEndpoints
{
    public static void MapWorkflow(this IEndpointRouteBuilder endpoints)
    {
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

using System.Security.Claims;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Features.Loans;

public static class WorkflowQueueEndpoints
{
    public static void MapWorkflowQueueEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans/queue")
            .WithTags("Workflow Queue")
            .RequireAuthorization();

        // GET /api/loans/queue/my — the reviewer's desk view:
        // queue positions, head flag, owners, and the caller's current claim.
        group.MapGet("/my", async (
            ClaimsPrincipal principal,
            IWorkflowQueueService queue,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var role = principal.GetRole();
            var branchCode = principal.GetBranchCode();

            var desk = await queue.GetDeskAsync(userId, role, branchCode, ct);
            return Results.Ok(ApiResponse<DeskQueueResponse>.SuccessResponse(desk));
        })
        .WithName("GetMyDeskQueue")
        .Produces<ApiResponse<DeskQueueResponse>>(200)
        .RequireAuthorization();

        // POST /api/loans/queue/claim — atomic head-lease.
        // Returns the claimed item, or null data when the desk is empty.
        group.MapPost("/claim", async (
            ClaimsPrincipal principal,
            IWorkflowQueueService queue,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var role = principal.GetRole();
            var branchCode = principal.GetBranchCode();

            var result = await queue.ClaimHeadAsync(userId, role, branchCode, ct);
            return result is null
                ? Results.Ok(ApiResponse<ClaimResponse?>.SuccessResponse(null, "Queue is clear — nothing to serve."))
                : Results.Ok(ApiResponse<ClaimResponse>.SuccessResponse(result, $"Serving {result.LamId}."));
        })
        .WithName("ClaimNextFromDesk")
        .Produces<ApiResponse<ClaimResponse?>>(200)
        .RequireAuthorization();

        // POST /api/loans/queue/release — release the current claim.
        // Clears OwnerUserId + LeasedAt, returns the item to the pool.
        group.MapPost("/release", async (
            ClaimsPrincipal principal,
            IWorkflowQueueService queue,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var released = await queue.ReleaseClaimAsync(userId, ct);
            return released
                ? Results.Ok(ApiResponse.SuccessResponse("Claim released."))
                : Results.Ok(ApiResponse.ErrorResponse("No active claim to release."));
        })
        .WithName("ReleaseDeskClaim")
        .Produces<ApiResponse>(200)
        .RequireAuthorization();

        group.MapPost("/{id:int}/claim", async (
            int id,
            ClaimsPrincipal principal,
            IWorkflowQueueService queue,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var role = principal.GetRole();
            var branchCode = principal.GetBranchCode();

            var result = await queue.ClaimByIdAsync(id, userId, role, branchCode, ct);
            return result switch
            {
                ClaimByIdResult.Claimed claimed =>
                    Results.Ok(ApiResponse<ClaimResponse>.SuccessResponse(claimed.Response, $"Serving {claimed.Response.LamId}.")),
                ClaimByIdResult.NotHead =>
                    Results.Conflict(ApiResponse<ClaimResponse>.ErrorResponse(
                        "This file is queued behind another application. Serve from the Review Desk in order.")),
                ClaimByIdResult.LeasedByOther leased =>
                    Results.Conflict(ApiResponse<ClaimResponse>.ErrorResponse(
                        $"Currently with {leased.OwnerName}.")),
                _ =>
                    Results.NotFound(ApiResponse<ClaimResponse>.ErrorResponse("Not in your desk queue.")),
            };
        })
        .WithName("ClaimQueueItemById")
        .Produces<ApiResponse<ClaimResponse>>(200)
        .Produces<ApiResponse<ClaimResponse>>(404)
        .Produces<ApiResponse<ClaimResponse>>(409)
        .RequireAuthorization();
    }
}
using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.Submit;
using Ebi.Alas.Api.Features.LoanApplications.WorkflowActions;
using Ebi.Alas.Api.Features.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications;

public static class LoanWorkflowEndpoints
{
    public static void MapLoanWorkflow(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/loans").WithTags("Loans").RequireAuthorization();

        group.MapPost("/{id:guid}/recommend", async (
            Guid id,
            LoanWorkflowService service,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var result = await service.RecommendAsync(id, caller, cancellationToken);
            return Results.Ok(result);
        });

        group.MapPost("/{id:guid}/evaluate", async (
            Guid id,
            EvaluateRequest request,
            LoanWorkflowService service,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var result = await service.EvaluateAsync(id, request.Recommend, caller, cancellationToken);
            return Results.Ok(result);
        });

        group.MapPost("/{id:guid}/approve", async (
            Guid id,
            LoanWorkflowService service,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var result = await service.ApproveAsync(id, caller, cancellationToken);
            return Results.Ok(result);
        });

        group.MapPost("/{id:guid}/reject", async (
            Guid id,
            LoanWorkflowService service,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var result = await service.RejectAsync(id, caller, cancellationToken);
            return Results.Ok(result);
        });

        group.MapPost("/{id:guid}/pushback", async (
            Guid id,
            PushbackRequest request,
            LoanWorkflowService service,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var result = await service.PushBackAsync(
                id,
                caller,
                request.Section,
                request.Comment,
                cancellationToken);
            return Results.Ok(result);
        });

        group.MapPost("/{id:guid}/cancel", async (
            Guid id,
            LoanWorkflowService service,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var result = await service.CancelAsync(id, caller, cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/{id:guid}/signature-chain", async (
            Guid id,
            Infrastructure.Persistence.AlasDbContext db,
            IWorkflowConfiguration workflow,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var loanQuery = db.LoanApplications.AsNoTracking().Where(l => l.Id == id);
            if (!caller.CanAccessAllBranches)
            {
                loanQuery = loanQuery.Where(l => l.BranchId == caller.BranchId);
            }

            if (!await loanQuery.AnyAsync(cancellationToken))
            {
                return Results.NotFound();
            }

            await workflow.EnsureLoadedAsync(cancellationToken);
            var slots = await SignatureChainService.GetDisplayChainAsync(
                db, id, workflow.RequireRecommendation, cancellationToken);
            return Results.Ok(slots);
        });
    }
}

public sealed record EvaluateRequest(bool Recommend);

public sealed record PushbackRequest(string Section, string Comment);

using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications;

public sealed record GroupLoanSummaryDto(
    Guid Id,
    string LamId,
    string Status,
    string BranchCode,
    string LoanNo,
    string ProductCode,
    string Product,
    decimal ProposedAmount,
    int UnresolvedDocs);

public sealed record LoanGroupResponseDto(
    string GroupNo,
    IReadOnlyList<GroupLoanSummaryDto> Loans);

public static class LoanGroupEndpoints
{
    public static void MapLoanGroups(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/loan-groups").WithTags("Loan Groups").RequireAuthorization();

        group.MapGet("/{groupNo}", async (
            string groupNo,
            AlasDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            if (string.IsNullOrWhiteSpace(groupNo))
            {
                throw new NotFoundException("Loan group", groupNo);
            }

            var query = db.LoanApplications.AsNoTracking()
                .Where(l => l.ApplicationGroupNo == groupNo);
            query = await LoanAccess.FilterAccessibleAsync(db, query, caller, ct);

            var loans = await query
                .OrderBy(l => l.CreatedAt)
                .Select(l => new GroupLoanSummaryDto(
                    l.Id,
                    l.LamId,
                    l.Status.ToString(),
                    l.BranchId,
                    l.LamId,
                    l.ProductCode,
                    l.Product,
                    l.Principal,
                    db.DocumentChecklistItems.Count(d => d.LoanApplicationId == l.Id && !d.IsSubmitted)))
                .ToListAsync(ct);

            if (loans.Count == 0)
            {
                throw new NotFoundException("Loan group", groupNo);
            }

            return Results.Ok(new LoanGroupResponseDto(groupNo, loans));
        })
        .WithName("GetLoanGroup");

        group.MapPut("/{groupNo}/status", async (
            string groupNo,
            GroupStatusRequest request,
            AlasDbContext db,
            WorkflowActions.LoanWorkflowService workflow,
            ClaimsPrincipal user,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            if (string.IsNullOrWhiteSpace(groupNo))
            {
                throw new NotFoundException("Loan group", groupNo);
            }

            var result = await LoanGroupStatusHandler.ApplyAsync(db, workflow, caller, groupNo, request, ct);
            return Results.Ok(new
            {
                success = true,
                message = $"Applied to {result.Succeeded} of {result.Succeeded + result.Failed} loan(s).",
                data = result,
                errors = Array.Empty<string>(),
                timestamp = timeProvider.GetUtcNow(),
            });
        })
        .RequireRateLimiting("write")
        .WithName("UpdateLoanGroupStatus");
    }
}

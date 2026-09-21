using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans.Endpoints;

public sealed record GroupStatusRequest(string Status, string? Comments, IReadOnlyList<int>? LoanIds);
public sealed record GroupLoanResult(int LoanId, string LamId, bool Succeeded, string? Error);
public sealed record GroupStatusResponse(int Succeeded, int Failed, IReadOnlyList<GroupLoanResult> Results);

public static class LoanGroups
{
    public static void MapLoanGroupEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loan-groups")
            .WithTags("Loan Groups")
            .RequireAuthorization();

        group.MapGet("/{groupNo}", GetGroupAsync)
            .WithName("GetLoanGroup")
            .Produces<ApiResponse<object>>(200)
            .Produces<ApiResponse>(404);

        group.MapPut("/{groupNo}/status", UpdateGroupStatusAsync)
            .WithName("UpdateGroupStatus")
            .Produces<ApiResponse<GroupStatusResponse>>(200)
            .Produces<ApiResponse>(404);
    }

    /// <summary>Single projected query — no Includes, no N+1 on the review page.</summary>
    private static async Task<IResult> GetGroupAsync(
        string groupNo, AppDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var actorRole = user.GetRole();
        var actorBranch = user.GetBranchCode();

        var loans = await db.LoanApplications.AsNoTracking()
            .Where(l => l.ApplicationGroupNo == groupNo)
            .OrderBy(l => l.Id)
            .Select(l => new
            {
                l.Id,
                l.LamId,
                l.Status,
                l.BranchCode,
                l.LoanNo,
                l.ProductCode,
                l.Product,
                ProposedAmount = l.ProposedAmount,
                UnresolvedDocs = l.DocumentChecklists.Count(d => d.Status == "Missing" || d.Status == "Pending"),
            })
            .ToListAsync(ct);

        if (loans.Count == 0)
            return Results.NotFound(ApiResponse.ErrorResponse("Loan group not found."));

        // Branch-scoped read: never reveal another branch's bundle (IDOR).
        if (actorRole != Roles.Admin && loans[0].BranchCode != actorBranch)
            return Results.NotFound(ApiResponse.ErrorResponse("Loan group not found."));

        return Results.Ok(ApiResponse<object>.SuccessResponse(new { groupNo, loans }));
    }

    /// <summary>
    /// Bundled review: one decision applied to every eligible loan in the group.
    /// Each loan runs the SAME per-loan pipeline (state machine, head-owner,
    /// guards, audit, queue, notifications) — eligibility is re-derived
    /// server-side per loan; the UI's pre-filter is a convenience only.
    /// Partial success is intentional and reported per loan.
    /// </summary>
    private static async Task<IResult> UpdateGroupStatusAsync(
        string groupNo, GroupStatusRequest request, AppDbContext db,
        ClaimsPrincipal user, ILoanStatusTransitionService transition,
        CancellationToken ct)
    {
        var actorRole = user.GetRole();
        var actorBranch = user.GetBranchCode();

        // Branch-scoped: verify the group exists and belongs to the actor's branch.
        var firstLoan = await db.LoanApplications.AsNoTracking()
            .Where(l => l.ApplicationGroupNo == groupNo)
            .Select(l => new { l.BranchCode })
            .FirstOrDefaultAsync(ct);

        if (firstLoan is null)
            return Results.NotFound(ApiResponse.ErrorResponse("Loan group not found."));

        if (actorRole != Roles.Admin && firstLoan.BranchCode != actorBranch)
            return Results.NotFound(ApiResponse.ErrorResponse("Loan group not found."));

        var ids = await db.LoanApplications.AsNoTracking()
            .Where(l => l.ApplicationGroupNo == groupNo
                        && (request.LoanIds == null || request.LoanIds.Contains(l.Id)))
            .Select(l => l.Id)
            .ToListAsync(ct);

        if (ids.Count == 0)
            return Results.NotFound(ApiResponse.ErrorResponse("No eligible loans found in group."));

        var results = new List<GroupLoanResult>(ids.Count);
        foreach (var id in ids)
        {
            var outcome = await transition.TryTransitionAsync(
                id, request.Status, request.Comments, null,
                null, null, user, ct);
            results.Add(new GroupLoanResult(id, outcome.LamId, outcome.Error == null, outcome.Error));
        }

        var succeeded = results.Count(r => r.Succeeded);
        return Results.Ok(ApiResponse<GroupStatusResponse>.SuccessResponse(
            new GroupStatusResponse(succeeded, results.Count - succeeded, results)));
    }
}

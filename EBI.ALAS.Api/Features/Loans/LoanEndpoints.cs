using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Loans.DTOs;
using EBI.ALAS.Api.Features.Loans.Endpoints;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

public static class LoanEndpoints
{
    public static void MapLoanEndpoints(this WebApplication app)
    {
        app.MapGetLoansEndpoints();
        app.MapGetLoanByIdEndpoints();
        app.MapGetLoanHistoryEndpoints();
        app.MapGetSlaPolicyEndpoints();
        app.MapGetQueueDefaultEndpoints();
        app.MapCreateLoanEndpoints();
        app.MapUpdateLoanStatusEndpoints();
        app.MapCancelLoanEndpoints();
        app.MapGetLoanTimelineEndpoints();
        app.MapDocumentFlagEndpoints();

        // POST /api/loans/{id}/assignment/release — release an active lease
        var group = app.MapGroup("/api/loans")
            .WithTags("Loans")
            .RequireAuthorization();

        group.MapPost("/{id:int}/assignment/release", async (
            int id,
            ClaimsPrincipal principal,
            ILoanAssignmentService assignment,
            IWorkflowQueueService queue,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            await assignment.ReleaseAsync(id, userId, ct);
            // Also clear the queue item lease so the desk recovers.
            await queue.ReleaseClaimAsync(userId, ct);
            return Results.Ok(ApiResponse.SuccessResponse("Assignment released."));
        })
        .WithName("ReleaseAssignment")
        .Produces<ApiResponse>(200)
        .RequireAuthorization("CanViewLoan");

        // GET /api/loans/{id}/routing — tier + matched rule + completeness
        group.MapGet("/{id:int}/routing", async (
            int id,
            AppDbContext db,
            IDocumentCompletenessService completeness,
            CancellationToken ct) =>
        {
            var loan = await db.LoanApplications
                .AsNoTracking()
                .Include(l => l.Deviations)
                .FirstOrDefaultAsync(l => l.Id == id, ct);

            if (loan is null)
                return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));

            var completenessResult = await completeness.CheckAsync(loan, ct);

            // Resolve assigned approver name
            string? assignedName = null;
            if (loan.AssignedApproverId is { } assigneeId)
            {
                var assignee = await db.Users.AsNoTracking()
                    .Where(u => u.Id == assigneeId)
                    .Select(u => new { u.FirstName, u.LastName })
                    .FirstOrDefaultAsync(ct);
                if (assignee is not null)
                    assignedName = $"{assignee.FirstName} {assignee.LastName}";
            }

            // Resolve matched rule description
            string? matchedRule = null;
            if (loan.RequiredApprovalTier is { } tier)
            {
                var auth = await db.ApprovalAuthorities.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Tier == tier, ct);
                if (auth is not null)
                    matchedRule = $"{auth.DisplayName} (Tier {tier})";
            }

            var dto = new LoanRoutingDto
            {
                LoanId = loan.Id,
                RequiredApprovalTier = loan.RequiredApprovalTier,
                DeviationSeverity = (int)loan.DeviationSeverity,
                TotalExposure = loan.TotalExposure,
                LoanType = loan.LoanType,
                MatchedRule = matchedRule,
                DocumentsComplete = completenessResult.Complete,
                MissingDocuments = completenessResult.Missing.ToList(),
                AssignedApproverId = loan.AssignedApproverId,
                AssignedApproverName = assignedName,
            };

            return Results.Ok(ApiResponse<LoanRoutingDto>.SuccessResponse(dto));
        })
        .WithName("GetLoanRouting")
        .Produces<ApiResponse<LoanRoutingDto>>(200)
        .Produces<ApiResponse>(404)
        .RequireAuthorization("CanViewLoan");

        // POST /api/loans/{id}/documents/verify — on-demand completeness recheck
        group.MapPost("/{id:int}/documents/verify", async (
            int id,
            ClaimsPrincipal principal,
            AppDbContext db,
            IDocumentCompletenessService completeness,
            IDocumentGateService gate,
            ITimeProvider time,
            CancellationToken ct) =>
        {
            var loan = await db.LoanApplications.FindAsync([id], ct);
            if (loan is null) return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));

            // Branch scoping: non-Admin users can only verify loans in their own branch.
            var userRole = principal.GetRole();
            if (userRole != Roles.Admin)
            {
                var branchCode = principal.GetBranchId();
                if (!string.Equals(loan.BranchCode, branchCode, StringComparison.Ordinal))
                    return Results.Forbid();
            }

            var result = await completeness.CheckByLoanNoAsync(loan.LoanNo, ct, bypassCache: true);
            loan.DocumentsCompleteAt = result.Complete ? time.UtcNow : null;
            await db.SaveChangesAsync(ct);

            // If the loan has a document flag and is now complete, clear it.
            if (result.Complete)
            {
                await gate.SyncAsync(loan, ct);
            }

            return Results.Ok(ApiResponse<object>.SuccessResponse(new
            {
                complete = result.Complete,
                missing = result.Missing,
                documentsCompleteAt = loan.DocumentsCompleteAt,
            }, result.Complete
                ? "All required documents are uploaded."
                : $"{result.Missing.Count} document(s) still missing."));
        })
        .WithName("VerifyLoanDocuments")
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiResponse>(404)
        .RequireAuthorization("CanViewLoan");
    }
}

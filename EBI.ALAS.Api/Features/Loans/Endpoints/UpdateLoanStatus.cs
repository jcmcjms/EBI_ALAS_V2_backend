using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Loans.DTOs;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Features.Presence;
using EBI.ALAS.Api.Infrastructure.Data;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans.Endpoints;

public static class UpdateLoanStatus
{
    public static void MapUpdateLoanStatusEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loans")
            .RequireAuthorization();

        group.MapPut("/{id:int}/status", async (
            int id,
            [FromBody] UpdateLoanStatusRequest request,
            IValidator<UpdateLoanStatusRequest> validator,
            ILoanRepository loanRepository,
            ILoanWorkflowService workflowService,
            IWorkflowQueueService queueService,
            IAuditLogger auditLogger,
            INotificationDispatcher notificationDispatcher,
            IRealtimeNotificationService realtimeService,
            IDocumentCompletenessService completenessService,
            IApprovalRoutingService routingService,
            ILoanAssignmentService assignmentService,
            ClaimsPrincipal user,
            ITimeProvider timeProvider,
            AppDbContext db,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray());

                return Results.BadRequest(ApiResponse.ErrorResponse(
                    "Validation failed",
                    errors.SelectMany(e => e.Value).ToList()));
            }

            var loan = await loanRepository.GetByIdAsync(id);
            if (loan == null)
            {
                return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));
            }

            var userRole = user.GetRole();
            var userId = user.GetUserId();

            var resolved = request.Action is { } action
                ? workflowService.ResolveAction(action, loan.Status)
                : new ResolvedAction(request.Status!, null);

            var targetStatus = resolved.TargetStatus;
            var verdict = resolved.Verdict;

            if (!workflowService.IsValidTransition(loan.Status, targetStatus, userRole))
            {
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    $"Invalid status transition from {loan.Status} to {targetStatus} for role {userRole}"));
            }

            // Queue ownership guard (review desks only; Admin bypass)
            if (WorkflowQueueService.StageForStatus(loan.Status) != null
                && userRole != Roles.Admin
                && !await queueService.IsHeadOwnerAsync(loan.Id, userId, loan.Status, ct))
            {
                return Results.Json(ApiResponse.ErrorResponse(
                    "It is not your turn: this application is queued behind the file currently on the desk."),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var fromStatus = loan.Status;
            var comments = request.Comments;

            if (fromStatus == "Draft" && targetStatus == "ForRecommendation")
            {
                var completeness = await completenessService.CheckAsync(loan, ct);
                loan.DocumentsCompleteAt = completeness.Complete ? timeProvider.UtcNow : null;
            }

            if (targetStatus == "ForApproval" && fromStatus == "ForChecking")
            {
                loan.DocumentsCompleteAt = timeProvider.UtcNow;

                var decision = await routingService.RouteAsync(loan, ct);
                loan.DeviationSeverity = decision.Severity;
                loan.RequiredApprovalTier = decision.Tier == 0 ? null : decision.Tier;
                loan.NoAuthorityReason = decision.NoAuthorityReason;
                await loanRepository.UpdateAsync(loan);

                if (decision.Tier > 0)
                    await assignmentService.AssignAsync(loan, ct);
            }

            if (fromStatus == "ForApproval" && userRole == Roles.Approver)
            {
                var me = await db.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == userId, ct);
                var inTier = me?.ApprovalAuthorityKey != null
                    && db.ApprovalAuthorities.Any(a => a.Key == me.ApprovalAuthorityKey
                        && a.Tier == loan.RequiredApprovalTier);
                var mineOrUnassigned = loan.AssignedApproverId is null || loan.AssignedApproverId == userId;
                if (!inTier || !mineOrUnassigned)
                    return Results.Json(ApiResponse.ErrorResponse(
                        $"This application requires a Tier {loan.RequiredApprovalTier} approver and must be assigned to you."),
                        statusCode: StatusCodes.Status403Forbidden);

                loan.AssignedApproverId = null;
                loan.AssignedAt = null;
            }

            // Apply the permission-based authorization that was registered but never used.
            // The four policies (CanRecommendLoan, CanEvaluateLoan, CanApproveLoan, CanRejectLoan)
            // were dead — zero endpoint references. Now we check the specific permission for the
            // target status. Admin bypasses (HasPermission returns true for Admin via wildcard).
            var requiredPermission = GetRequiredPermission(targetStatus, verdict);
            if (!string.IsNullOrEmpty(requiredPermission) && !user.HasPermission(requiredPermission))
            {
                return Results.Json(ApiResponse.ErrorResponse(
                    $"You do not have the required permission ({requiredPermission}) for this transition."),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            // Reviewer flag: document flagging is now handled by the dedicated
            // POST /api/loans/{id}/document-flag endpoint. The status never changes
            // for missing documents — flag columns + checklist state record the fact.

            var actionName = (fromStatus, targetStatus, verdict) switch
            {
                ("ForChecking", "ForApproval", "NotRecommended") => "EvaluatedNotRecommended",
                ("ForChecking", "ForApproval", "Recommended")    => "EvaluatedRecommended",
                (_, "ForRevision", _)                            => "PushedBack",
                _                                                => "StatusChanged",
            };

            // Wrap the critical state changes in a transaction.
            // Previously, the status update, queue mutations, and audit log were
            // 4+N independent SaveChanges calls. A failure at step 3 left the loan
            // in a desk with no queue item; a failure at step 4 meant no audit record.
            // Now all four operations commit atomically.
            // Note: Notifications and SignalR calls stay OUTSIDE the transaction —
            // they are fire-and-forget and should not block the state change.
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);

                loan.Status = targetStatus;
                loan.LastActionDate = timeProvider.UtcNow;

                await loanRepository.UpdateAsync(loan);

                // Queue lifecycle: dequeue old desk, enqueue new desk
                var oldStage = WorkflowQueueService.StageForStatus(fromStatus);
                var newStage = WorkflowQueueService.StageForStatus(targetStatus);

                if (oldStage != null)
                    await queueService.DequeueAndPromoteAsync(loan, fromStatus, ct);
                if (newStage != null)
                    await queueService.EnqueueAsync(loan, targetStatus, ct);

                await auditLogger.LogActionAsync(
                    id, userId, actionName, fromStatus, targetStatus, comments);

                await tx.CommitAsync(ct);
            });

            // Delegate notification fan-out to NotificationDispatcher.
            // This replaces ~100 lines of near-duplicated notification code with
            // a single call. The dispatcher handles batching (1 SaveChanges) and
            // realtime sends (SignalR) for all transition types.
            await notificationDispatcher.DispatchTransitionNotificationsAsync(
                loan, fromStatus, targetStatus, verdict, comments, actionName, user, ct);

            await realtimeService.NotifyDashboardUpdateAsync(loan.BranchCode);

            var response = new LoanResponse
            {
                Id = loan.Id,
                LamId = loan.LamId,
                ApplicationGroupNo = loan.ApplicationGroupNo,
                BranchCode = loan.BranchCode,
                LoanNo = loan.LoanNo,
                ProductCode = loan.ProductCode,
                Product = loan.Product,
                CisId = loan.CisId,
                FirstName = loan.FirstName,
                MiddleName = loan.MiddleName,
                LastName = loan.LastName,
                Agency = loan.Agency,
                Position = loan.Position,
                EmployeeId = loan.EmployeeId,
                NetTakeHomePay = loan.NetTakeHomePay,
                School = loan.School,
                Referrer = loan.Referrer,
                Purpose = loan.Purpose,
                ProposedAmount = loan.ProposedAmount,
                TermDays = loan.TermDays,
                InterestRate = loan.InterestRate,
                PolicyTermMonths = loan.PolicyTermMonths,
                ApprovalTermDays = loan.ApprovalTermDays,
                AnnualRatePercent = loan.AnnualRatePercent,
                Status = loan.Status,
                ApplicationDate = loan.ApplicationDate,
                LastActionDate = loan.LastActionDate,
                CreatedById = loan.CreatedById,
                CreatedByName = user.GetFirstName() + " " + user.GetLastName(),
                EvaluationVerdict = actionName.StartsWith("Evaluated", StringComparison.Ordinal)
                    ? actionName : null,
            };

            return Results.Ok(ApiResponse<LoanResponse>.SuccessResponse(response, "Loan status updated successfully"));
        })
        .WithName("UpdateLoanStatus")
        .Produces<ApiResponse<LoanResponse>>(200)
        .Produces<ApiResponse>(400)
        .Produces<ApiResponse>(404);
    }

    /// <summary>
    /// Maps a target status transition to the required permission.
    /// Returns null for transitions that don't require a specific permission
    /// (e.g., Draft → ForRecommendation is gated by workflow validation only).
    /// </summary>
    private static string? GetRequiredPermission(string targetStatus, string? verdict) => targetStatus switch
    {
        "ForRecommendation" => Permissions.LoansRecommend,
        "ForChecking" => verdict == "NotRecommended" || verdict == "Recommended"
            ? Permissions.LoansEvaluate   // Evaluation verdict
            : Permissions.LoansView,      // Resubmission from revision
        "ForApproval" => Permissions.LoansEvaluate, // Evaluator recommending
        "Approved" => Permissions.LoansApprove,
        "Rejected" => Permissions.LoansReject,
        "ForRevision" => Permissions.LoansRecommend, // Pushback from recommender
        _ => null, // Draft, ForDisbursement, Disbursed, OnGoing, Cancelled — no specific permission
    };
}

public sealed record UpdateLoanStatusRequest
{
    public WorkflowAction? Action { get; init; }
    public string? Status { get; init; }
    public string? Comments { get; init; }
}

public sealed class UpdateLoanStatusValidator : AbstractValidator<UpdateLoanStatusRequest>
{
    private static readonly string[] KnownStatuses =
    [
        "Draft", "ForRecommendation", "ForChecking", "ForApproval",
        "Approved", "Rejected", "ForRevision", "ForDisbursement",
        "Disbursed", "OnGoing", "Cancelled",
    ];

    private static readonly WorkflowAction[] RemarkHeavy =
    [
        WorkflowAction.PushBack, WorkflowAction.NotRecommend,
        WorkflowAction.Reject, WorkflowAction.ReturnForRevision,
    ];

    public UpdateLoanStatusValidator()
    {
        RuleFor(x => x)
            .Must(x => x.Action is null ^ string.IsNullOrWhiteSpace(x.Status))
            .WithMessage("Supply exactly one of: action (desk intent) or status (direct transition).");

        RuleFor(x => x.Status)
            .Must(s => KnownStatuses.Contains(s))
            .When(x => x.Action is null && !string.IsNullOrWhiteSpace(x.Status))
            .WithMessage(x => $"'{x.Status}' is not a valid workflow status.");

        RuleFor(x => x.Comments)
            .NotEmpty()
            .When(x => x.Action is not null && RemarkHeavy.Contains(x.Action.Value))
            .WithMessage("Remarks are required for this workflow action.");

        RuleFor(x => x.Comments)
            .MinimumLength(10)
            .When(x => x.Action is not null && RemarkHeavy.Contains(x.Action.Value))
            .WithMessage("Push-back, rejection and a Not Recommended evaluation require at least 10 characters of remarks.");
    }
}

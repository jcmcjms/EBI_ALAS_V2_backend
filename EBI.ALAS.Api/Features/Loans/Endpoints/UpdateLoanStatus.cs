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
            IDocumentChecklistStore checklistStore,
            IApprovalRoutingService routingService,
            ILoanAssignmentService assignmentService,
            IDocumentGateService documentGateService,
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

            if (!workflowService.IsValidTransition(loan.Status, request.Status, userRole))
            {
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    $"Invalid status transition from {loan.Status} to {request.Status} for role {userRole}"));
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

            if (fromStatus == "Draft" && request.Status == "ForRecommendation")
            {
                var completeness = await completenessService.CheckAsync(loan, ct);
                loan.DocumentsCompleteAt = completeness.Complete ? timeProvider.UtcNow : null;
            }

            if (request.Status == "ForApproval" && fromStatus == "ForChecking")
            {
                var decision = await routingService.RouteAsync(loan, ct);
                loan.DeviationSeverity = decision.Severity;
                loan.RequiredApprovalTier = decision.Tier;
                await loanRepository.UpdateAsync(loan);

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

            var verdict = request.Verdict;
            if (fromStatus == "ForChecking" && request.Status == "ForApproval"
                && userRole == Roles.Evaluator)
            {
                if (verdict is not ("Recommended" or "NotRecommended"))
                    return Results.BadRequest(ApiResponse.ErrorResponse(
                        "An evaluation verdict ('Recommended' or 'NotRecommended') is required for this transition."));
            }
            else if (verdict is not null)
            {
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    "A verdict is only accepted on the evaluator's ForChecking → ForApproval transition."));
            }

            // Apply the permission-based authorization that was registered but never used.
            // The four policies (CanRecommendLoan, CanEvaluateLoan, CanApproveLoan, CanRejectLoan)
            // were dead — zero endpoint references. Now we check the specific permission for the
            // target status. Admin bypasses (HasPermission returns true for Admin via wildcard).
            var requiredPermission = GetRequiredPermission(request.Status, verdict);
            if (!string.IsNullOrEmpty(requiredPermission) && !user.HasPermission(requiredPermission))
            {
                return Results.Json(ApiResponse.ErrorResponse(
                    $"You do not have the required permission ({requiredPermission}) for this transition."),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            // Reviewer flag: route through DocumentGateService (the ONLY entry
            // path into ForIncompleteDocuments — no automatic holds).
            if (request.Status == "ForIncompleteDocuments")
            {
                // Validator guarantees non-empty codes + comment; re-check here
                // so the invariant holds even for non-FluentValidation callers.
                if (request.MissingRequirementCodes is not { Count: > 0 } || string.IsNullOrWhiteSpace(comments))
                    return Results.UnprocessableEntity(ApiResponse.ErrorResponse(
                        "Flagging a file requires at least one missing requirement and a written reason."));

                await documentGateService.FlagIncompleteAsync(loan, request.MissingRequirementCodes, comments, userId, ct);
                await realtimeService.NotifyDashboardUpdateAsync(loan.BranchCode);
                return Results.Ok(ApiResponse.SuccessResponse("File flagged as lacking documents."));
            }

            // Reviewer proceeding with a flagged file: justification is mandatory.
            if (fromStatus == "ForIncompleteDocuments" && request.Status is "ForApproval" or "ForRecommendation"
                && string.IsNullOrWhiteSpace(comments))
            {
                return Results.UnprocessableEntity(ApiResponse.ErrorResponse(
                    "Proceeding with missing documents requires a written justification."));
            }

            if (fromStatus == "ForIncompleteDocuments" && request.Status == "ForChecking")
            {
                if (request.SubmittedRequirementCodes is { Count: > 0 })
                    await checklistStore.MarkSubmittedAsync(
                        loan.Id, request.SubmittedRequirementCodes, userId, ct);

                var unresolved = await checklistStore.GetUnresolvedAsync(loan.Id, ct);
                if (unresolved.Count > 0)
                {
                    var names = string.Join(", ", unresolved.Select(u => u.Name));
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["submittedRequirementCodes"] = [$"Documents still incomplete: {names}"],
                    });
                }
            }

            var actionName = (fromStatus, request.Status, verdict) switch
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

                loan.Status = request.Status;
                loan.LastActionDate = timeProvider.UtcNow;

                await loanRepository.UpdateAsync(loan);

                // Queue lifecycle: dequeue old desk, enqueue new desk
                var oldStage = WorkflowQueueService.StageForStatus(fromStatus);
                var newStage = WorkflowQueueService.StageForStatus(request.Status);

                if (oldStage != null)
                    await queueService.DequeueAndPromoteAsync(loan, fromStatus, ct);
                if (newStage != null)
                    await queueService.EnqueueAsync(loan, request.Status, ct);

                await auditLogger.LogActionAsync(
                    id, userId, actionName, fromStatus, request.Status, comments);

                await tx.CommitAsync(ct);
            });

            // Delegate notification fan-out to NotificationDispatcher.
            // This replaces ~100 lines of near-duplicated notification code with
            // a single call. The dispatcher handles batching (1 SaveChanges) and
            // realtime sends (SignalR) for all transition types.
            await notificationDispatcher.DispatchTransitionNotificationsAsync(
                loan, fromStatus, request.Status, verdict, comments, actionName, user, ct);

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

public class UpdateLoanStatusRequest
{
    public string Status { get; init; } = string.Empty;
    public string? Comments { get; init; }

    /// <summary>Evaluator-only verdict for ForChecking → ForApproval.
    /// Persisted as the audit action name so the approver sees the stance
    /// without a schema migration.</summary>
    public string? Verdict { get; init; }

    /// <summary>
    /// Required when Status == "ForIncompleteDocuments". The checklist
    /// requirement codes the evaluator flags as missing.
    /// </summary>
    public IReadOnlyList<string>? MissingRequirementCodes { get; init; }

    /// <summary>
    /// Used when transitioning from ForIncompleteDocuments → ForChecking.
    /// The checklist requirement codes the encoder marks as submitted.
    /// </summary>
    public IReadOnlyList<string>? SubmittedRequirementCodes { get; init; }
}

public class UpdateLoanStatusValidator : AbstractValidator<UpdateLoanStatusRequest>
{
    private static readonly string[] ValidStatuses = new[]
    {
        "Draft", "ForRecommendation", "ForChecking", "ForApproval",
        "Approved", "Rejected", "ForRevision", "ForDisbursement",
        "Disbursed", "OnGoing", "ForIncompleteDocuments",
        "Cancelled"
    };

    public UpdateLoanStatusValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty().WithMessage("Status is required")
            .Must(s => ValidStatuses.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", ValidStatuses)}");

        RuleFor(x => x.Verdict)
            .Must(v => v is null or "Recommended" or "NotRecommended")
            .WithMessage("Verdict must be 'Recommended' or 'NotRecommended'.");

        When(x => x.Verdict == "NotRecommended"
                  || x.Status == "ForRevision"
                  || x.Status == "Rejected", () =>
        {
            RuleFor(x => x.Comments)
                .NotEmpty().MinimumLength(10)
                .WithMessage("Comments (min 10 characters) are required for pushbacks, rejections, and a Not Recommended evaluation.");
        });

        // ForIncompleteDocuments requires at least one missing requirement code
        // and a written reason (reviewer-initiated flag, not system-held).
        When(x => x.Status == "ForIncompleteDocuments", () =>
        {
            RuleFor(x => x.MissingRequirementCodes)
                .NotEmpty().WithMessage("At least one missing requirement code is required.")
                .ForEach(c => c.NotEmpty().MaximumLength(64));

            RuleFor(x => x.Comments)
                .NotEmpty().WithMessage("A reason is required when flagging a file as lacking documents.")
                .MinimumLength(5).WithMessage("Reason must be at least 5 characters.");
        });

        When(x => x.SubmittedRequirementCodes != null, () =>
        {
            RuleFor(x => x.SubmittedRequirementCodes!)
                .ForEach(c => c.NotEmpty().MaximumLength(64));
        });

        RuleFor(x => x.Comments).MaximumLength(2000)
            .WithMessage("Comments must not exceed 2000 characters");
    }
}

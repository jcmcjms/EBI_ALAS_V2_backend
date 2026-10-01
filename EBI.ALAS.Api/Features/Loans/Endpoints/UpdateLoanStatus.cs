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
            var skipQueue = false;
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
                else
                    skipQueue = true;
            }
            if (fromStatus == "ForApproval" && userRole == Roles.Approver)
            {
                var me = await db.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == userId, ct);
                var inTier = me?.ApprovalAuthorityKey is not null
                    && await db.ApprovalAuthorities.AnyAsync(
                        a => a.Key == me.ApprovalAuthorityKey
                          && a.Tier == loan.RequiredApprovalTier,
                        ct);
                var mineOrUnassigned = loan.AssignedApproverId is null || loan.AssignedApproverId == userId;
                if (!inTier || !mineOrUnassigned)
                    return Results.Json(ApiResponse.ErrorResponse(
                        $"This application requires a Tier {loan.RequiredApprovalTier} approver and must be assigned to you."),
                        statusCode: StatusCodes.Status403Forbidden);
                loan.AssignedApproverId = null;
                loan.AssignedAt = null;
            }
            var requiredPermission = GetRequiredPermission(targetStatus, verdict);
            if (!string.IsNullOrEmpty(requiredPermission) && !user.HasPermission(requiredPermission))
            {
                return Results.Json(ApiResponse.ErrorResponse(
                    $"You do not have the required permission ({requiredPermission}) for this transition."),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            var actionName = (fromStatus, targetStatus, verdict) switch
            {
                ("ForChecking", "ForApproval", "NotRecommended") => "EvaluatedNotRecommended",
                ("ForChecking", "ForApproval", "Recommended")    => "EvaluatedRecommended",
                (_, "ForRevision", _)                            => "PushedBack",
                _                                                => "StatusChanged",
            };
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                loan.Status = targetStatus;
                loan.LastActionDate = timeProvider.UtcNow;
                await loanRepository.UpdateAsync(loan);
                var oldStage = WorkflowQueueService.StageForStatus(fromStatus);
                var newStage = WorkflowQueueService.StageForStatus(targetStatus);
                if (oldStage != null)
                    await queueService.DequeueAndPromoteAsync(loan, fromStatus, ct);
                if (newStage != null && !skipQueue)
                    await queueService.EnqueueAsync(loan, targetStatus, ct);
                await auditLogger.LogActionAsync(
                    id, userId, actionName, fromStatus, targetStatus, comments);
                await tx.CommitAsync(ct);
            });
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
                CDocStamp = loan.CDocStamp,
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
    private static string? GetRequiredPermission(string targetStatus, string? verdict) => targetStatus switch
    {
        "ForRecommendation" => Permissions.LoansRecommend,
        "ForChecking" => verdict == "NotRecommended" || verdict == "Recommended"
            ? Permissions.LoansEvaluate
            : Permissions.LoansView,
        "ForApproval" => Permissions.LoansEvaluate,
        "Approved" => Permissions.LoansApprove,
        "Rejected" => Permissions.LoansReject,
        "ForRevision" => Permissions.LoansRecommend,
        _ => null,
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

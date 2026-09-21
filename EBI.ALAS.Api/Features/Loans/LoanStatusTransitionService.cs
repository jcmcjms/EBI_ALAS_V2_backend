using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Features.Presence;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Result of a single loan status transition attempt.
/// </summary>
public sealed record LoanTransitionResult(string LamId, string? Error);

/// <summary>
/// Extracted transition logic shared by the single-loan endpoint and the
/// bundled group endpoint. Reuses the same guards (workflow validity,
/// queue ownership, completeness checks) so both callers enforce
/// identical rules.
/// </summary>
public interface ILoanStatusTransitionService
{
    Task<LoanTransitionResult> TryTransitionAsync(
        int loanId, string targetStatus, string? comments, string? verdict,
        IReadOnlyList<string>? missingCodes, IReadOnlyList<string>? submittedCodes,
        ClaimsPrincipal user, CancellationToken ct);
}

public sealed class LoanStatusTransitionService(
    ILoanRepository loanRepo,
    ILoanWorkflowService workflow,
    IWorkflowQueueService queueService,
    IAuditLogger auditLogger,
    IDocumentChecklistStore checklistStore,
    IDocumentCompletenessService completenessService,
    IApprovalRoutingService routingService,
    ILoanAssignmentService assignmentService,
    INotificationService notificationService,
    IRealtimeNotificationService realtimeService,
    ITimeProvider timeProvider) : ILoanStatusTransitionService
{
    public async Task<LoanTransitionResult> TryTransitionAsync(
        int loanId, string targetStatus, string? comments, string? verdict,
        IReadOnlyList<string>? missingCodes, IReadOnlyList<string>? submittedCodes,
        ClaimsPrincipal user, CancellationToken ct)
    {
        var loan = await loanRepo.GetByIdAsync(loanId);
        if (loan is null)
            return new LoanTransitionResult("?", "Loan not found.");

        var userRole = user.GetRole();
        var userId = user.GetUserId();
        var fromStatus = loan.Status;

        if (!workflow.IsValidTransition(fromStatus, targetStatus, userRole))
            return new LoanTransitionResult(loan.LamId,
                $"Invalid transition from {fromStatus} to {targetStatus} for role {userRole}.");

        // Queue ownership guard (Admin and System bypass)
        if (WorkflowQueueService.StageForStatus(fromStatus) != null
            && userRole != Roles.Admin
            && userRole != Roles.System
            && !await queueService.IsHeadOwnerAsync(loanId, userId, fromStatus, ct))
        {
            return new LoanTransitionResult(loan.LamId,
                "It is not your turn: this application is queued behind the file currently on the desk.");
        }

        // Document completeness transitions
        if (targetStatus == "ForIncompleteDocuments")
        {
            // Server derives the authoritative pending set from the document
            // server — client-supplied missingCodes are ignored for trust.
            var items = await completenessService.GetItemsByLoanNoAsync(loan.LoanNo, ct);
            var pending = items.Where(i => i.UploadStatus != "Uploaded").Select(i => i.IdCode).ToList();

            if (pending.Count == 0)
                return new LoanTransitionResult(loan.LamId,
                    "All requirements are already uploaded — nothing to push back.");

            loan.DocumentsCompleteAt = null; // invalidate completeness stamp

            // Record the authoritative missing list in the audit comment
            comments = $"{comments} | Missing: {string.Join(", ", pending)}";

            await checklistStore.MarkMissingAsync(loanId, pending, userId, ct);
        }

        if (fromStatus == "ForIncompleteDocuments" && targetStatus == "ForChecking")
        {
            if (submittedCodes is { Count: > 0 })
                await checklistStore.MarkSubmittedAsync(loanId, submittedCodes, userId, ct);

            var unresolved = await checklistStore.GetUnresolvedAsync(loanId, ct);
            if (unresolved.Count > 0)
            {
                var names = string.Join(", ", unresolved.Select(u => u.Name));
                return new LoanTransitionResult(loan.LamId, $"Documents still incomplete: {names}");
            }
        }

        // Approval routing (same as UpdateLoanStatus endpoint)
        if (targetStatus == "ForApproval" && fromStatus == "ForChecking")
        {
            var completeness = await completenessService.CheckAsync(loan, ct);
            if (!completeness.Complete)
                return new LoanTransitionResult(loan.LamId, "Application cannot proceed to approval: incomplete documents.");

            loan.DocumentsCompleteAt = timeProvider.UtcNow;
            var decision = await routingService.RouteAsync(loan, ct);
            loan.DeviationSeverity = decision.Severity;
            loan.RequiredApprovalTier = decision.Tier;
            await loanRepo.UpdateAsync(loan);
            await assignmentService.AssignAsync(loan, ct);
        }

        var actionName = (fromStatus, targetStatus, verdict) switch
        {
            ("ForChecking", "ForApproval", "NotRecommended") => "EvaluatedNotRecommended",
            ("ForChecking", "ForApproval", "Recommended") => "EvaluatedRecommended",
            (_, "ForRevision", _) => "PushedBack",
            _ => "StatusChanged",
        };

        loan.Status = targetStatus;
        loan.LastActionDate = timeProvider.UtcNow;
        await loanRepo.UpdateAsync(loan);

        // Queue lifecycle
        var oldStage = WorkflowQueueService.StageForStatus(fromStatus);
        var newStage = WorkflowQueueService.StageForStatus(targetStatus);
        if (oldStage != null)
            await queueService.DequeueAndPromoteAsync(loan, fromStatus, ct);
        if (newStage != null)
            await queueService.EnqueueAsync(loan, targetStatus, ct);

        await auditLogger.LogActionAsync(
            loanId, userId, actionName, fromStatus, targetStatus, comments);

        // Notification routing (simplified — single-loan endpoint has richer routing)
        var link = $"/loans/monitoring?id={loanId}";
        var actorName = $"{user.GetFirstName()} {user.GetLastName()}";
        var clientName = $"{loan.FirstName} {loan.LastName}";

        if (targetStatus == "ForIncompleteDocuments")
        {
            await notificationService.CreateAsync(loan.CreatedById,
                "Documents Incomplete — Action Required",
                $"{actorName} flagged {clientName}'s application ({loan.LamId}) as having incomplete documents.",
                link);
            await realtimeService.NotifyUserAsync(loan.CreatedById,
                "Documents Incomplete — Action Required",
                $"{actorName} flagged {clientName}'s application ({loan.LamId}) as having incomplete documents.",
                link);
        }

        if (loan.CreatedById != userId)
        {
            await notificationService.CreateAsync(loan.CreatedById,
                $"Status Update: {targetStatus}",
                $"Your application for {clientName} ({loan.LamId}) has been updated to {targetStatus}.",
                link);
            await realtimeService.NotifyUserAsync(loan.CreatedById,
                $"Status Update: {targetStatus}",
                $"Your application for {clientName} ({loan.LamId}) has been updated to {targetStatus}.",
                link);
        }

        await realtimeService.NotifyDashboardUpdateAsync(loan.BranchCode);

        return new LoanTransitionResult(loan.LamId, null);
    }
}

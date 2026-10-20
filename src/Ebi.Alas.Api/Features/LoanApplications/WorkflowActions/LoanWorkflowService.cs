using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications.WorkflowActions;

public sealed class LoanWorkflowService(
    AlasDbContext db,
    WorkflowQueueService queue,
    Notifications.IRealtimeNotifier? notifier,
    Notifications.ApplicationArrivalNotifier arrivals,
    TimeProvider timeProvider)
{
    public async Task<LoanResponse> RecommendAsync(
        Guid loanId,
        CallerContext caller,
        CancellationToken cancellationToken,
        string? remarks = null)
    {
        var loan = await LoadScopedAsync(loanId, caller, cancellationToken);
        if (loan.Status == LoanStatus.ForChecking && IsEvaluator(caller))
        {
            return await EvaluateAsync(loanId, recommend: true, caller, cancellationToken, remarks);
        }

        EnsureRole(caller, UserRole.Recommender);
        var fromRecommend = loan.Status;
        var comments = NormalizeRemarks(remarks);
        loan.TransitionTo(LoanStatus.ForChecking, timeProvider.GetUtcNow(), caller.UserId, "Recommended");
        RecordAction(loan, caller.UserId, "Recommended", fromRecommend.ToString(), loan.Status.ToString(), comments, timeProvider.GetUtcNow());
        await ClearReviewDraftsAsync(loan.Id, cancellationToken);
        loan.AssignTo(null, timeProvider.GetUtcNow());
        await queue.CompleteAsync(await ActiveQueueIdAsync(loanId, WorkflowStage.Recommendation, cancellationToken), caller.UserId, cancellationToken);
        await queue.EnqueueAsync(loanId, WorkflowStage.Evaluation, loan.BranchId, cancellationToken);
        await SignatureChainService.SignAsync(db, loanId, "Recommender", caller.UserId, timeProvider.GetUtcNow(), cancellationToken);
        await NotifyAsync(caller.UserId, "Application recommended for checking", $"Loan {loan.LamId} was recommended and is ready for evaluation.", cancellationToken);
        await arrivals.NotifyRoleAsync(
            UserRole.Evaluator,
            loan.BranchId,
            "New application ready for evaluation",
            $"Loan {loan.LamId} was recommended and is ready for evaluation.",
            link: $"/loans/approval/{loan.Id}",
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await NotifyDashboardAsync(cancellationToken);
        return await loan.ToResponseAsync(db, cancellationToken);
    }

    private static bool IsEvaluator(CallerContext caller) =>
        caller.Role == UserRole.Evaluator || caller.CanAccessAllBranches;

    public async Task<LoanResponse> EvaluateAsync(
        Guid loanId,
        bool recommend,
        CallerContext caller,
        CancellationToken cancellationToken,
        string? remarks = null)
    {
        EnsureRole(caller, UserRole.Evaluator);
        var loan = await LoadScopedAsync(loanId, caller, cancellationToken);
        var fromEval = loan.Status;
        var evalVerb = recommend ? "EvaluatedRecommended" : "EvaluatedNotRecommended";
        loan.TransitionTo(LoanStatus.ForApproval, timeProvider.GetUtcNow(), caller.UserId, evalVerb);
        RecordAction(loan, caller.UserId, evalVerb, fromEval.ToString(), loan.Status.ToString(), NormalizeRemarks(remarks), timeProvider.GetUtcNow());
        await ClearReviewDraftsAsync(loan.Id, cancellationToken);
        loan.AssignTo(null, timeProvider.GetUtcNow());
        await queue.CompleteAsync(await ActiveQueueIdAsync(loanId, WorkflowStage.Evaluation, cancellationToken), caller.UserId, cancellationToken);
        await queue.EnqueueAsync(loanId, WorkflowStage.Approval, loan.BranchId, cancellationToken);
        await SignatureChainService.SignAsync(db, loanId, "Evaluator", caller.UserId, timeProvider.GetUtcNow(), cancellationToken);
        await NotifyAsync(
            caller.UserId,
            recommend ? "Application recommended for approval" : "Application not recommended",
            recommend
                ? $"Loan {loan.LamId} was evaluated as Recommended and sent for approval."
                : $"Loan {loan.LamId} was evaluated as Not Recommended and sent for approval.",
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await NotifyDashboardAsync(cancellationToken);
        return await loan.ToResponseAsync(db, cancellationToken);
    }

    public async Task<LoanResponse> ApproveAsync(
        Guid loanId,
        CallerContext caller,
        CancellationToken cancellationToken,
        string? remarks = null)
    {
        EnsureRole(caller, UserRole.Approver);
        var loan = await LoadScopedAsync(loanId, caller, cancellationToken);
        var fromApprove = loan.Status;
        loan.TransitionTo(LoanStatus.Approved, timeProvider.GetUtcNow(), caller.UserId, "Approved");
        RecordAction(loan, caller.UserId, "Approved", fromApprove.ToString(), loan.Status.ToString(), NormalizeRemarks(remarks), timeProvider.GetUtcNow());
        await ClearReviewDraftsAsync(loan.Id, cancellationToken);
        await queue.CompleteAsync(await ActiveQueueIdAsync(loanId, WorkflowStage.Approval, cancellationToken), caller.UserId, cancellationToken);
        await SignatureChainService.SignAsync(db, loanId, "Approver", caller.UserId, timeProvider.GetUtcNow(), cancellationToken);
        await NotifyAsync(caller.UserId, "Application approved", $"Loan {loan.LamId} has been approved.", cancellationToken);
        await NotifyEncoderAsync(
            loan,
            caller.UserId,
            "Application approved",
            $"Your application {loan.LamId} has been approved.",
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await NotifyDashboardAsync(cancellationToken);
        return await loan.ToResponseAsync(db, cancellationToken);
    }

    public async Task<LoanResponse> RejectAsync(
        Guid loanId,
        CallerContext caller,
        CancellationToken cancellationToken,
        string? remarks = null)
    {
        EnsureRole(caller, UserRole.Approver);
        var loan = await LoadScopedAsync(loanId, caller, cancellationToken);
        var fromReject = loan.Status;
        loan.TransitionTo(LoanStatus.Rejected, timeProvider.GetUtcNow(), caller.UserId, "Rejected");
        RecordAction(loan, caller.UserId, "Rejected", fromReject.ToString(), loan.Status.ToString(), NormalizeRemarks(remarks), timeProvider.GetUtcNow());
        await ClearReviewDraftsAsync(loan.Id, cancellationToken);
        await queue.CompleteAsync(await ActiveQueueIdAsync(loanId, WorkflowStage.Approval, cancellationToken), caller.UserId, cancellationToken);
        await NotifyAsync(caller.UserId, "Application rejected", $"Loan {loan.LamId} has been rejected.", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await NotifyDashboardAsync(cancellationToken);
        return await loan.ToResponseAsync(db, cancellationToken);
    }

    public async Task<LoanResponse> PushBackAsync(
        Guid loanId,
        CallerContext caller,
        string section,
        string comment,
        CancellationToken cancellationToken)
    {
        EnsureRole(caller, UserRole.Recommender, UserRole.Evaluator, UserRole.Approver);
        var loan = await LoadScopedAsync(loanId, caller, cancellationToken);
        var fromPush = loan.Status;
        loan.TransitionTo(LoanStatus.ForRevision, timeProvider.GetUtcNow(), caller.UserId, "PushedBack");
        RecordAction(loan, caller.UserId, "PushedBack", fromPush.ToString(), loan.Status.ToString(), comment, timeProvider.GetUtcNow());
        await ClearReviewDraftsAsync(loan.Id, cancellationToken);
        loan.AssignTo(loan.CreatedBy, timeProvider.GetUtcNow());
        db.RevisionRequests.Add(Revisions.RevisionRequest.Create(
            loanId, section, comment, caller.UserId, timeProvider.GetUtcNow()));
        await CompleteActiveQueueAsync(loanId, caller.UserId, cancellationToken);
        await NotifyAsync(
            caller.UserId,
            "Application returned for revision",
            $"Loan {loan.LamId} was sent back to the encoder for revision.",
            cancellationToken,
            link: $"/loans/approval/{loan.Id}");
        var encoderBody = string.IsNullOrWhiteSpace(comment)
            ? $"Your application {loan.LamId} was returned for revision."
            : $"Your application {loan.LamId} was returned for revision. Remarks: {comment.Trim()}";
        await NotifyEncoderAsync(
            loan,
            caller.UserId,
            "Application returned for revision",
            encoderBody,
            cancellationToken,
            link: $"/loans/revision/{loan.Id}");
        await db.SaveChangesAsync(cancellationToken);
        if (notifier is not null)
        {
            await notifier.NotifyLoanAssignedAsync(loan.Id, loan.CreatedBy, cancellationToken);
        }
        await NotifyDashboardAsync(cancellationToken);
        return await loan.ToResponseAsync(db, cancellationToken);
    }

    private async Task CompleteActiveQueueAsync(
        Guid loanId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var active = await db.WorkflowQueueItems
            .FirstOrDefaultAsync(
                q => q.LoanApplicationId == loanId && q.State == QueueItemState.Active,
                cancellationToken);
        if (active is null)
        {
            return;
        }

        await queue.CompleteAsync(active.Id, userId, cancellationToken);
    }

    public async Task<LoanResponse> CancelAsync(
        Guid loanId,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        var loan = await LoadScopedAsync(loanId, caller, cancellationToken);
        if (!caller.CanAccessAllBranches && loan.CreatedBy != caller.UserId)
        {
            EnsureRole(caller, UserRole.Approver);
        }

        loan.Cancel(timeProvider.GetUtcNow(), caller.UserId);
        loan.AssignTo(null, timeProvider.GetUtcNow());
        await ClearReviewDraftsAsync(loanId, cancellationToken);
        await queue.CompleteOpenForApplicationAsync(loanId, cancellationToken);
        await NotifyAsync(caller.UserId, "Application cancelled", $"Loan {loan.LamId} has been cancelled.", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await NotifyDashboardAsync(cancellationToken);
        return await loan.ToResponseAsync(db, cancellationToken);
    }

    private async Task NotifyDashboardAsync(CancellationToken cancellationToken)
    {
        if (notifier is null)
        {
            return;
        }

        await notifier.NotifyDashboardUpdatedAsync(cancellationToken);
    }

    private static void EnsureRole(CallerContext caller, params UserRole[] allowed)
    {
        if (caller.CanAccessAllBranches)
        {
            return;
        }

        if (!allowed.Contains(caller.Role))
        {
            throw new ForbiddenException("You are not allowed to perform this workflow action.");
        }
    }

    private async Task<LoanApplication> LoadScopedAsync(
        Guid loanId,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        var query = db.LoanApplications.Where(l => l.Id == loanId);
        if (!caller.CanViewAllLoanBranches)
        {
            query = query.Where(l => l.BranchId == caller.BranchId);
        }

        return await query.FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Loan", loanId.ToString());
    }

    private async Task<Guid> ActiveQueueIdAsync(Guid loanId, WorkflowStage stage, CancellationToken cancellationToken) =>
        await db.WorkflowQueueItems
            .Where(q => q.LoanApplicationId == loanId && q.Stage == stage && q.State == QueueItemState.Active)
            .Select(q => q.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private void RecordAction(
        LoanApplication loan,
        Guid actorId,
        string action,
        string? fromStatus,
        string? toStatus,
        string? comments,
        DateTimeOffset at)
    {
        db.LoanApplicationActions.Add(LoanApplicationAction.Create(
            loan.Id, actorId, action, fromStatus, toStatus, comments, at));
    }

    private static string? NormalizeRemarks(string? remarks)
    {
        if (string.IsNullOrWhiteSpace(remarks))
        {
            return null;
        }

        var trimmed = remarks.Trim();
        if (trimmed.Length > Domain.ReviewDraft.MaxRemarksLength)
        {
            throw new ValidationException(
                $"Remarks must be {Domain.ReviewDraft.MaxRemarksLength} characters or fewer.");
        }

        return trimmed;
    }

    private async Task ClearReviewDraftsAsync(Guid loanId, CancellationToken cancellationToken)
    {
        var drafts = await db.ReviewDrafts
            .Where(d => d.LoanApplicationId == loanId)
            .ToListAsync(cancellationToken);
        if (drafts.Count > 0)
        {
            db.ReviewDrafts.RemoveRange(drafts);
        }
    }

    private async Task NotifyAsync(
        Guid actorId,
        string title,
        string body,
        CancellationToken cancellationToken,
        string? link = null)
    {
        var notification = Notification.Create(
            actorId,
            title,
            body,
            NotificationType.Action,
            timeProvider.GetUtcNow());
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);
        if (notifier is not null)
        {
            await notifier.NotifyUserAsync(
                notification.Id,
                actorId,
                title,
                body,
                link,
                notification.CreatedAt,
                cancellationToken);
        }
    }

    private async Task NotifyEncoderAsync(
        LoanApplication loan,
        Guid actorId,
        string title,
        string body,
        CancellationToken cancellationToken,
        string? link = null)
    {
        if (loan.CreatedBy == actorId)
        {
            return;
        }

        await NotifyAsync(loan.CreatedBy, title, body, cancellationToken, link);
    }
}

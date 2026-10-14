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
        CancellationToken cancellationToken)
    {
        var loan = await LoadScopedAsync(loanId, caller, cancellationToken);
        if (loan.Status == LoanStatus.ForChecking && IsEvaluator(caller))
        {
            return await EvaluateAsync(loanId, recommend: true, caller, cancellationToken);
        }

        EnsureRole(caller, UserRole.Recommender);
        loan.TransitionTo(LoanStatus.ForChecking, timeProvider.GetUtcNow(), caller.UserId, "Recommended");
        loan.AssignTo(null, timeProvider.GetUtcNow());
        await queue.CompleteAsync(await ActiveQueueIdAsync(loanId, WorkflowStage.Recommendation, cancellationToken), caller.UserId, cancellationToken);
        await queue.EnqueueAsync(loanId, WorkflowStage.Evaluation, loan.BranchId, cancellationToken);
        await NotifyAsync(caller.UserId, $"Loan {loan.LamId} recommended", cancellationToken);
        await arrivals.NotifyRoleAsync(
            UserRole.Evaluator,
            loan.BranchId,
            "New application ready for evaluation",
            $"Loan {loan.LamId} was recommended and is ready for evaluation.",
            link: $"/loans/approval/{loan.Id}",
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await loan.ToResponseAsync(db, cancellationToken);
    }

    private static bool IsEvaluator(CallerContext caller) =>
        caller.Role == UserRole.Evaluator || caller.CanAccessAllBranches;

    public async Task<LoanResponse> EvaluateAsync(
        Guid loanId,
        bool recommend,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        EnsureRole(caller, UserRole.Evaluator);
        var loan = await LoadScopedAsync(loanId, caller, cancellationToken);
        loan.TransitionTo(LoanStatus.ForApproval, timeProvider.GetUtcNow(), caller.UserId, recommend ? "EvaluatedRecommended" : "EvaluatedNotRecommended");
        loan.AssignTo(null, timeProvider.GetUtcNow());
        await queue.CompleteAsync(await ActiveQueueIdAsync(loanId, WorkflowStage.Evaluation, cancellationToken), caller.UserId, cancellationToken);
        await queue.EnqueueAsync(loanId, WorkflowStage.Approval, loan.BranchId, cancellationToken);
        await NotifyAsync(caller.UserId, $"Loan {loan.LamId} evaluated (recommend={recommend})", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await loan.ToResponseAsync(db, cancellationToken);
    }

    public async Task<LoanResponse> ApproveAsync(
        Guid loanId,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        EnsureRole(caller, UserRole.Approver);
        var loan = await LoadScopedAsync(loanId, caller, cancellationToken);
        loan.TransitionTo(LoanStatus.Approved, timeProvider.GetUtcNow(), caller.UserId, "Approved");
        await queue.CompleteAsync(await ActiveQueueIdAsync(loanId, WorkflowStage.Approval, cancellationToken), caller.UserId, cancellationToken);
        await NotifyAsync(caller.UserId, $"Loan {loan.LamId} approved", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await loan.ToResponseAsync(db, cancellationToken);
    }

    public async Task<LoanResponse> RejectAsync(
        Guid loanId,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        EnsureRole(caller, UserRole.Approver);
        var loan = await LoadScopedAsync(loanId, caller, cancellationToken);
        loan.TransitionTo(LoanStatus.Rejected, timeProvider.GetUtcNow(), caller.UserId, "Rejected");
        await queue.CompleteAsync(await ActiveQueueIdAsync(loanId, WorkflowStage.Approval, cancellationToken), caller.UserId, cancellationToken);
        await NotifyAsync(caller.UserId, $"Loan {loan.LamId} rejected", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
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
        loan.TransitionTo(LoanStatus.ForRevision, timeProvider.GetUtcNow(), caller.UserId, "PushedBack");
        loan.AssignTo(loan.CreatedBy, timeProvider.GetUtcNow());
        db.RevisionRequests.Add(Revisions.RevisionRequest.Create(
            loanId, section, comment, caller.UserId, timeProvider.GetUtcNow()));
        await CompleteActiveQueueAsync(loanId, caller.UserId, cancellationToken);
        await NotifyAsync(caller.UserId, $"Loan {loan.LamId} returned for revision", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
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
        await NotifyAsync(caller.UserId, $"Loan {loan.LamId} cancelled", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await loan.ToResponseAsync(db, cancellationToken);
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
        if (!caller.CanAccessAllBranches)
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

    private async Task NotifyAsync(Guid actorId, string title, CancellationToken cancellationToken)
    {
        var notification = Notification.Create(
            actorId,
            title,
            string.Empty,
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
                string.Empty,
                link: null,
                notification.CreatedAt,
                cancellationToken);
        }
    }
}

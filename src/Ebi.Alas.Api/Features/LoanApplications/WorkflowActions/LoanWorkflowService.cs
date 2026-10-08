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
    TimeProvider timeProvider)
{
    public async Task<LoanResponse> RecommendAsync(
        Guid loanId,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        EnsureRole(caller, UserRole.Recommender);
        var loan = await LoadScopedAsync(loanId, caller, cancellationToken);
        loan.TransitionTo(LoanStatus.ForChecking, timeProvider.GetUtcNow());
        await queue.CompleteAsync(await ActiveQueueIdAsync(loanId, WorkflowStage.Recommendation, cancellationToken), caller.UserId, cancellationToken);
        await queue.EnqueueAsync(loanId, WorkflowStage.Evaluation, loan.BranchId, cancellationToken);
        await NotifyAsync(caller.UserId, $"Loan {loan.LamId} recommended", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return LoanMapping.ToResponse(loan);
    }

    public async Task<LoanResponse> EvaluateAsync(
        Guid loanId,
        bool recommend,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        EnsureRole(caller, UserRole.Evaluator);
        var loan = await LoadScopedAsync(loanId, caller, cancellationToken);
        loan.TransitionTo(LoanStatus.ForApproval, timeProvider.GetUtcNow());
        await queue.CompleteAsync(await ActiveQueueIdAsync(loanId, WorkflowStage.Evaluation, cancellationToken), caller.UserId, cancellationToken);
        await queue.EnqueueAsync(loanId, WorkflowStage.Approval, loan.BranchId, cancellationToken);
        await NotifyAsync(caller.UserId, $"Loan {loan.LamId} evaluated (recommend={recommend})", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return LoanMapping.ToResponse(loan);
    }

    public async Task<LoanResponse> ApproveAsync(
        Guid loanId,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        EnsureRole(caller, UserRole.Approver);
        var loan = await LoadScopedAsync(loanId, caller, cancellationToken);
        loan.TransitionTo(LoanStatus.Approved, timeProvider.GetUtcNow());
        await queue.CompleteAsync(await ActiveQueueIdAsync(loanId, WorkflowStage.Approval, cancellationToken), caller.UserId, cancellationToken);
        await NotifyAsync(caller.UserId, $"Loan {loan.LamId} approved", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return LoanMapping.ToResponse(loan);
    }

    public async Task<LoanResponse> RejectAsync(
        Guid loanId,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        EnsureRole(caller, UserRole.Approver);
        var loan = await LoadScopedAsync(loanId, caller, cancellationToken);
        loan.TransitionTo(LoanStatus.Rejected, timeProvider.GetUtcNow());
        await queue.CompleteAsync(await ActiveQueueIdAsync(loanId, WorkflowStage.Approval, cancellationToken), caller.UserId, cancellationToken);
        await NotifyAsync(caller.UserId, $"Loan {loan.LamId} rejected", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return LoanMapping.ToResponse(loan);
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
        loan.TransitionTo(LoanStatus.ForRevision, timeProvider.GetUtcNow());
        db.RevisionRequests.Add(Revisions.RevisionRequest.Create(
            loanId, section, comment, caller.UserId, timeProvider.GetUtcNow()));
        await NotifyAsync(caller.UserId, $"Loan {loan.LamId} returned for revision", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return LoanMapping.ToResponse(loan);
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

        loan.Cancel(timeProvider.GetUtcNow());
        await NotifyAsync(caller.UserId, $"Loan {loan.LamId} cancelled", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return LoanMapping.ToResponse(loan);
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
        db.Notifications.Add(Notification.Create(
            actorId,
            title,
            string.Empty,
            NotificationType.Action,
            timeProvider.GetUtcNow()));
        if (notifier is not null)
        {
            await notifier.NotifyUserAsync(actorId, title, string.Empty, cancellationToken);
        }
    }
}

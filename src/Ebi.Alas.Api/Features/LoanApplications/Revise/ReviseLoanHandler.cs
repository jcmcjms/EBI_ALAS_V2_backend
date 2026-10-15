using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanComputation;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications.Revise;

public sealed class ReviseLoanHandler(
    AlasDbContext db,
    LoanComputationService computation,
    WorkflowQueueService queue,
    IWorkflowConfiguration workflow,
    ApplicationArrivalNotifier arrivals,
    IRealtimeNotifier? realtime,
    TimeProvider timeProvider)
{
    public async Task<LoanResponse> HandleAsync(
        Guid loanId,
        ReviseLoanCommand command,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var notes = command.RevisionNotes?.Trim() ?? string.Empty;
        if (notes.Length < 10)
        {
            throw new ValidationException("Revision notes are required and must be at least 10 characters.");
        }

        var query = db.LoanApplications.Where(l => l.Id == loanId);
        if (!caller.CanAccessAllBranches)
        {
            query = query.Where(l => l.BranchId == caller.BranchId);
        }

        var loan = await query.FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Loan", loanId.ToString());

        if (loan.Status != LoanStatus.ForRevision)
        {
            throw new ConflictException($"Only applications in ForRevision can be revised. Current status: {loan.Status}.");
        }

        if (!string.IsNullOrWhiteSpace(command.RowVersion))
        {
            byte[] expected;
            try
            {
                expected = Convert.FromBase64String(command.RowVersion);
            }
            catch (FormatException)
            {
                throw new ConflictException("Invalid row version token.");
            }

            if (!loan.RowVersion.AsSpan().SequenceEqual(expected))
            {
                throw new ConflictException("This application was modified by another user. Reload and try again.");
            }
        }

        var now = timeProvider.GetUtcNow();
        var computed = computation.Compute(new LoanComputationInput(
            command.Principal,
            command.TermDays,
            command.InterestRatePerMonth));

        var formJson = command.Form is { } form ? form.GetRawText() : null;
        loan.ApplyRevision(
            string.IsNullOrWhiteSpace(command.ClientName) ? loan.ClientName : command.ClientName,
            command.Principal,
            command.TermDays,
            command.InterestRatePerMonth,
            computed.TotalInterest,
            computed.TotalDeductions,
            computed.NetProceeds,
            formJson,
            now);

        await workflow.EnsureLoadedAsync(cancellationToken);
        var next = workflow.RequireRecommendation
            ? LoanStatus.ForRecommendation
            : LoanStatus.ForChecking;
        loan.TransitionTo(next, now, caller.UserId, "Revised");

        db.LoanApplicationActions.Add(LoanApplicationAction.Create(
            loan.Id,
            caller.UserId,
            "Revised",
            LoanStatus.ForRevision.ToString(),
            next.ToString(),
            notes,
            now));

        var open = await db.RevisionRequests
            .Where(r => r.LoanApplicationId == loanId && r.ResolvedAt == null)
            .ToListAsync(cancellationToken);
        var requesterIds = open.Select(r => r.RequestedBy).Distinct().ToList();
        foreach (var revision in open)
        {
            revision.Resolve(now);
        }

        await db.SaveChangesAsync(cancellationToken);

        await Deviations.LoanDeviationSync.SyncFromFormAsync(db, loan, loan.FormJson, cancellationToken);

        var stage = workflow.RequireRecommendation
            ? WorkflowStage.Recommendation
            : WorkflowStage.Evaluation;
        await queue.EnqueueAsync(loan.Id, stage, loan.BranchId, cancellationToken);

        var deskRole = workflow.RequireRecommendation
            ? UserRole.Recommender
            : UserRole.Evaluator;
        var deskTitle = workflow.RequireRecommendation
            ? "Application resubmitted for recommendation"
            : "Application resubmitted for evaluation";
        var deskBody = $"Loan {loan.LamId} was resubmitted after corrections and is ready for review.";
        await arrivals.NotifyRoleAsync(
            deskRole,
            loan.BranchId,
            deskTitle,
            deskBody,
            link: $"/loans/approval/{loan.Id}",
            cancellationToken);

        foreach (var requesterId in requesterIds.Where(id => id != caller.UserId))
        {
            await NotifyUserAsync(
                requesterId,
                "Application resubmitted after corrections",
                $"Loan {loan.LamId} was revised and resubmitted. Your revision requests are resolved.",
                link: $"/loans/approval/{loan.Id}",
                cancellationToken);
        }

        if (realtime is not null)
        {
            await realtime.NotifyDashboardUpdatedAsync(cancellationToken);
        }

        return loan.ToResponse();
    }

    private async Task NotifyUserAsync(
        Guid userId,
        string title,
        string body,
        string? link,
        CancellationToken cancellationToken)
    {
        var notification = Notification.Create(
            userId,
            title,
            body,
            NotificationType.Action,
            timeProvider.GetUtcNow());
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);
        if (realtime is null)
        {
            return;
        }

        await realtime.NotifyUserAsync(
            notification.Id,
            userId,
            title,
            body,
            link,
            notification.CreatedAt,
            cancellationToken);
    }
}

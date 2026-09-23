using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Features.Presence;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Extracted notification fan-out from UpdateLoanStatus endpoint.
/// The original endpoint had ~250 lines of near-duplicated notification code
/// (6 branches × 2 calls each = 12+ call sites). This dispatcher centralizes
/// the routing logic and uses batched writes (single SaveChanges per call).
/// </summary>
public interface INotificationDispatcher
{
    Task DispatchTransitionNotificationsAsync(
        LoanApplication loan,
        string fromStatus,
        string toStatus,
        string? verdict,
        string? comments,
        string actionName,
        ClaimsPrincipal user,
        CancellationToken ct);
}

public sealed class NotificationDispatcher(
    ILoanRepository loanRepository,
    INotificationService notificationService,
    IRealtimeNotificationService realtimeService,
    AppDbContext db) : INotificationDispatcher
{
    public async Task DispatchTransitionNotificationsAsync(
        LoanApplication loan,
        string fromStatus,
        string toStatus,
        string? verdict,
        string? comments,
        string actionName,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var link = $"/loans/monitoring?id={loan.Id}";
        var actorName = $"{user.GetFirstName()} {user.GetLastName()}";
        var clientName = $"{loan.FirstName} {loan.LastName}";
        var userId = user.GetUserId();
        var userRole = user.GetRole();

        var batch = new List<NotificationDraft>();
        var realtimeSends = new List<(int UserId, string Title, string Description, string? Link)>();

        // Transition-specific notifications
        if (toStatus == "ForChecking" && fromStatus != "ForIncompleteDocuments")
        {
            var evaluators = await loanRepository.GetUsersByRoleAndBranchAsync(
                Roles.Evaluator, loan.BranchCode, ct);
            var title = "Ready for Evaluation";
            var desc = $"{actorName} recommended {clientName}'s application ({loan.LamId}).";
            foreach (var e in evaluators)
            {
                batch.Add(new NotificationDraft(e.Id, title, desc, link, NotificationTypes.Action));
                realtimeSends.Add((e.Id, title, desc, link));
            }
        }
        else if (toStatus == "ForRecommendation")
        {
            var recommenders = await loanRepository.GetUsersByRoleAndBranchAsync(
                Roles.Recommender, loan.BranchCode, ct);
            var title = "Ready for Recommendation";
            var desc = $"{actorName} resubmitted {clientName}'s application ({loan.LamId}) for recommendation.";
            foreach (var r in recommenders)
            {
                batch.Add(new NotificationDraft(r.Id, title, desc, link, NotificationTypes.Action));
                realtimeSends.Add((r.Id, title, desc, link));
            }
        }
        else if (toStatus == "ForApproval")
        {
            var approvers = await loanRepository.GetUsersByRoleAndBranchAsync(
                Roles.Approver, loan.BranchCode, ct);
            var stance = verdict == "NotRecommended" ? "NOT RECOMMENDED" : "RECOMMENDED";
            var extra = verdict == "NotRecommended" ? $" Evaluator remarks: {comments}" : string.Empty;
            var title = verdict == "NotRecommended" ? "Evaluation: NOT Recommended" : "Ready for Approval";
            var desc = $"{actorName} evaluated {clientName}'s application ({loan.LamId}) as {stance}.{extra}";
            foreach (var a in approvers)
            {
                batch.Add(new NotificationDraft(a.Id, title, desc, link, NotificationTypes.Action));
                realtimeSends.Add((a.Id, title, desc, link));
            }
        }
        else if (toStatus == "ForRevision")
        {
            var pushbackRole = userRole == Roles.Recommender ? "Branch Head"
                             : userRole == Roles.Approver ? "Area Head"
                             : "Reviewer";
            var title = "Application Returned for Revision";
            var desc = $"{pushbackRole} {actorName} returned {clientName}'s application ({loan.LamId}). Reason: {comments}";
            batch.Add(new NotificationDraft(loan.CreatedById, title, desc, link, NotificationTypes.Action));
            realtimeSends.Add((loan.CreatedById, title, desc, link));
        }
        else if (toStatus == "ForIncompleteDocuments")
        {
            var title = "Documents Incomplete — Action Required";
            var desc = $"{actorName} flagged {clientName}'s application ({loan.LamId}) as having incomplete documents. Reason: {comments}";
            batch.Add(new NotificationDraft(loan.CreatedById, title, desc, link, NotificationTypes.Action));
            realtimeSends.Add((loan.CreatedById, title, desc, link));
        }
        else if (toStatus == "ForChecking" && fromStatus == "ForIncompleteDocuments")
        {
            var lastFlagAction = await db.LoanActions.AsNoTracking()
                .Where(a => a.LoanApplicationId == loan.Id && a.ToStatus == "ForIncompleteDocuments")
                .OrderByDescending(a => a.ActionDate)
                .FirstOrDefaultAsync(ct);

            if (lastFlagAction != null)
            {
                var title = "Documents Resubmitted — Ready for Review";
                var desc = $"{actorName} resubmitted documents for {clientName}'s application ({loan.LamId}).";
                batch.Add(new NotificationDraft(lastFlagAction.ActionByUserId, title, desc, link, NotificationTypes.Action));
                realtimeSends.Add((lastFlagAction.ActionByUserId, title, desc, link));
            }
        }

        // Always notify the creator (if different from actor)
        if (loan.CreatedById != userId)
        {
            var title = $"Status Update: {toStatus}";
            var desc = $"Your application for {clientName} ({loan.LamId}) has been updated to {toStatus}.";
            batch.Add(new NotificationDraft(loan.CreatedById, title, desc, link, NotificationTypes.Message));
            realtimeSends.Add((loan.CreatedById, title, desc, link));
        }

        // Single batched DB write for all notifications
        if (batch.Count > 0)
            await notificationService.CreateBatchAsync(batch);

        // Realtime sends (SignalR) — fire-and-forget, outside transaction
        foreach (var send in realtimeSends)
            await realtimeService.NotifyUserAsync(send.UserId, send.Title, send.Description, send.Link);
    }
}

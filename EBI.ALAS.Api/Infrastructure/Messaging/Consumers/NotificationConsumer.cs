using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Infrastructure.Messaging.Events;
using MassTransit;
namespace EBI.ALAS.Api.Infrastructure.Messaging.Consumers;
public sealed class NotificationConsumer :
    IConsumer<NotificationCreatedEvent>,
    IConsumer<LoanStatusChangedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly IRealtimeNotificationService _realtimeService;
    private readonly ILoanRepository _loanRepository;
    private readonly ILogger<NotificationConsumer> _logger;
    public NotificationConsumer(
        INotificationService notificationService,
        IRealtimeNotificationService realtimeService,
        ILoanRepository loanRepository,
        ILogger<NotificationConsumer> logger)
    {
        _notificationService = notificationService;
        _realtimeService = realtimeService;
        _loanRepository = loanRepository;
        _logger = logger;
    }
    public async Task Consume(ConsumeContext<NotificationCreatedEvent> context)
    {
        var msg = context.Message;
        _logger.LogInformation(
            "Processing notification for User {UserId}: {Title}",
            msg.UserId, msg.Title);
        if (msg.Type is not null)
        {
            await _notificationService.CreateBatchAsync(
            [
                new NotificationDraft(msg.UserId, msg.Title, msg.Description, msg.Link, msg.Type)
            ]);
        }
        else
        {
            await _notificationService.CreateAsync(
                msg.UserId,
                msg.Title,
                msg.Description,
                msg.Link);
        }
        await _realtimeService.NotifyUserAsync(
            msg.UserId,
            msg.Title,
            msg.Description,
            msg.Link);
    }
    public async Task Consume(ConsumeContext<LoanStatusChangedEvent> context)
    {
        var msg = context.Message;
        _logger.LogInformation(
            "Processing loan status change notifications: Loan {LoanId} {From} → {To}",
            msg.LoanId, msg.FromStatus, msg.ToStatus);
        var link = $"/loans/monitoring?id={msg.LoanId}";
        var batch = new List<NotificationDraft>();
        var realtimeSends = new List<(int UserId, string Title, string Description, string? Link)>();
        if (msg.ToStatus == "ForChecking")
        {
            var evaluators = await _loanRepository.GetUsersByRoleAndBranchAsync(
                Roles.Evaluator, msg.BranchCode, context.CancellationToken);
            var title = "Ready for Evaluation";
            var desc = $"{msg.ActorName} recommended {msg.ClientName}'s application ({msg.LamId}).";
            foreach (var e in evaluators)
            {
                batch.Add(new NotificationDraft(e.Id, title, desc, link, NotificationTypes.Action));
                realtimeSends.Add((e.Id, title, desc, link));
            }
        }
        else if (msg.ToStatus == "ForRecommendation")
        {
            var recommenders = await _loanRepository.GetUsersByRoleAndBranchAsync(
                Roles.Recommender, msg.BranchCode, context.CancellationToken);
            var title = "Ready for Recommendation";
            var desc = $"{msg.ActorName} resubmitted {msg.ClientName}'s application ({msg.LamId}) for recommendation.";
            foreach (var r in recommenders)
            {
                batch.Add(new NotificationDraft(r.Id, title, desc, link, NotificationTypes.Action));
                realtimeSends.Add((r.Id, title, desc, link));
            }
        }
        else if (msg.ToStatus == "ForApproval")
        {
            var approvers = await _loanRepository.GetUsersByRoleAndBranchAsync(
                Roles.Approver, msg.BranchCode, context.CancellationToken);
            var stance = msg.Verdict == "NotRecommended" ? "NOT RECOMMENDED" : "RECOMMENDED";
            var extra = msg.Verdict == "NotRecommended" ? $" Evaluator remarks: {msg.Comments}" : string.Empty;
            var title = msg.Verdict == "NotRecommended" ? "Evaluation: NOT Recommended" : "Ready for Approval";
            var desc = $"{msg.ActorName} evaluated {msg.ClientName}'s application ({msg.LamId}) as {stance}.{extra}";
            foreach (var a in approvers)
            {
                batch.Add(new NotificationDraft(a.Id, title, desc, link, NotificationTypes.Action));
                realtimeSends.Add((a.Id, title, desc, link));
            }
        }
        else if (msg.ToStatus == "ForRevision")
        {
            var pushbackRole = msg.UserRole == Roles.Recommender ? "Branch Head"
                             : msg.UserRole == Roles.Approver ? "Area Head"
                             : "Reviewer";
            var title = "Application Returned for Revision";
            var desc = $"{pushbackRole} {msg.ActorName} returned {msg.ClientName}'s application ({msg.LamId}). Reason: {msg.Comments}";
            batch.Add(new NotificationDraft(msg.LoanCreatedById, title, desc, link, NotificationTypes.Action));
            realtimeSends.Add((msg.LoanCreatedById, title, desc, link));
        }
        if (msg.LoanCreatedById != msg.ActorUserId)
        {
            var title = $"Status Update: {msg.ToStatus}";
            var desc = $"Your application for {msg.ClientName} ({msg.LamId}) has been updated to {msg.ToStatus}.";
            batch.Add(new NotificationDraft(msg.LoanCreatedById, title, desc, link, NotificationTypes.Message));
            realtimeSends.Add((msg.LoanCreatedById, title, desc, link));
        }
        if (batch.Count > 0)
            await _notificationService.CreateBatchAsync(batch);
        foreach (var send in realtimeSends)
            await _realtimeService.NotifyUserAsync(send.UserId, send.Title, send.Description, send.Link);
    }
}
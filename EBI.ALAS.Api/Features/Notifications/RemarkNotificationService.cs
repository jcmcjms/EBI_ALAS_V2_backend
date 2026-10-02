namespace EBI.ALAS.Api.Features.Notifications;

public sealed record RemarkNotifyRequest(
    int LoanId,
    int ActorUserId,
    string Title,
    string Description,
    string? Link,
    int? PrimaryRecipientId);

public interface IRemarkNotificationService
{
    Task NotifyAsync(RemarkNotifyRequest request, CancellationToken ct = default);
}

public sealed class RemarkNotificationService : IRemarkNotificationService
{
    private readonly INotificationService _notifications;
    private readonly IRealtimeNotificationService _realtime;

    public RemarkNotificationService(INotificationService notifications, IRealtimeNotificationService realtime)
    {
        _notifications = notifications;
        _realtime = realtime;
    }

    public async Task NotifyAsync(RemarkNotifyRequest request, CancellationToken ct = default)
    {
        if (request.PrimaryRecipientId is { } recipientId && recipientId != request.ActorUserId)
        {
            await _notifications.CreateAsync(recipientId, request.Title, request.Description, request.Link);
            await _realtime.NotifyUserAsync(recipientId, request.Title, request.Description, request.Link);
        }

        var exclude = new List<int> { request.ActorUserId };
        if (request.PrimaryRecipientId is { } excludeId)
            exclude.Add(excludeId);

        await _realtime.NotifyEntityWatchersAsync(
            request.LoanId, request.Title, request.Description, request.Link, exclude);
    }
}

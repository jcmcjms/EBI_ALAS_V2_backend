using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Infrastructure.Messaging.Events;
using MassTransit;

namespace EBI.ALAS.Api.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumes notification events and persists + pushes them in real-time.
/// Decouples notification delivery from the loan workflow response path.
/// </summary>
public sealed class NotificationConsumer : IConsumer<NotificationCreatedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly IRealtimeNotificationService _realtimeService;
    private readonly ILogger<NotificationConsumer> _logger;

    public NotificationConsumer(
        INotificationService notificationService,
        IRealtimeNotificationService realtimeService,
        ILogger<NotificationConsumer> logger)
    {
        _notificationService = notificationService;
        _realtimeService = realtimeService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<NotificationCreatedEvent> context)
    {
        var msg = context.Message;

        _logger.LogInformation(
            "Processing notification for User {UserId}: {Title}",
            msg.UserId, msg.Title);

        // Persist to database — use explicit type if provided, otherwise
        // the service's CreateAsync will classify from the title.
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

        // Push via SignalR for instant bell update + toast
        await _realtimeService.NotifyUserAsync(
            msg.UserId,
            msg.Title,
            msg.Description,
            msg.Link);
    }
}

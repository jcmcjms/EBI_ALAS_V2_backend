using System.Security.Claims;
using Ebi.Alas.Api.Features.Presence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Ebi.Alas.Api.Features.Notifications;

public interface IRealtimeNotifier
{
    Task NotifyUserAsync(Guid userId, string title, string body, CancellationToken cancellationToken);
}

public sealed record ReceiveNotificationPayload(
    string Title,
    string Description,
    string? Link,
    DateTimeOffset Timestamp);

public sealed class NotificationHub(PresenceService presence) : Hub
{
    public const string Route = "/hubs/notifications";
    public const string ReceiveNotificationEvent = "ReceiveNotification";

    public static string UserGroup(Guid userId) => $"user:{userId}";

    public static ReceiveNotificationPayload ToReceiveNotification(
        string title,
        string body,
        DateTimeOffset createdAt) =>
        new(title, body, Link: null, Timestamp: createdAt);

    public override async Task OnConnectedAsync()
    {
        var sub = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Context.User?.FindFirstValue("sub");
        if (Guid.TryParse(sub, out var userId) && userId != Guid.Empty)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
            await Groups.AddToGroupAsync(Context.ConnectionId, PresenceEvents.Group);
            var online = await presence.GetOnlineUsersAsync(Context.ConnectionAborted);
            await Clients.Caller.SendAsync(PresenceEvents.Snapshot, online, Context.ConnectionAborted);
        }

        await base.OnConnectedAsync();
    }
}

public sealed class SignalRRealtimeNotifier(
    IHubContext<NotificationHub> hubContext,
    TimeProvider timeProvider) : IRealtimeNotifier
{
    public async Task NotifyUserAsync(Guid userId, string title, string body, CancellationToken cancellationToken)
    {
        var payload = NotificationHub.ToReceiveNotification(
            title,
            body,
            timeProvider.GetUtcNow());

        await hubContext.Clients.Group(NotificationHub.UserGroup(userId))
            .SendAsync(NotificationHub.ReceiveNotificationEvent, payload, cancellationToken);
    }
}

public static class NotificationHubModule
{
    public static void MapNotificationHub(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHub<NotificationHub>(NotificationHub.Route).RequireAuthorization();
}

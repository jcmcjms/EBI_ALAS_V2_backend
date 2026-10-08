using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Ebi.Alas.Api.Features.Notifications;

public interface IRealtimeNotifier
{
    Task NotifyUserAsync(Guid userId, string title, string body, CancellationToken cancellationToken);
}

public sealed class NotificationHub : Hub
{
    public const string Route = "/hubs/notifications";

    public static string UserGroup(Guid userId) => $"user:{userId}";
}

public sealed class SignalRRealtimeNotifier(
    IHubContext<NotificationHub> hubContext,
    TimeProvider timeProvider) : IRealtimeNotifier
{
    public async Task NotifyUserAsync(Guid userId, string title, string body, CancellationToken cancellationToken)
    {
        await hubContext.Clients.Group(NotificationHub.UserGroup(userId))
            .SendAsync("notification", new
            {
                title,
                body,
                createdAt = timeProvider.GetUtcNow()
            }, cancellationToken);
    }
}

public static class NotificationHubModule
{
    public static void MapNotificationHub(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHub<NotificationHub>(NotificationHub.Route).RequireAuthorization();
}

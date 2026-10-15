using System.Security.Claims;
using Ebi.Alas.Api.Features.Presence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Ebi.Alas.Api.Features.Notifications;

public interface IRealtimeNotifier
{
    Task NotifyUserAsync(
        Guid notificationId,
        Guid userId,
        string title,
        string body,
        string? link,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken);

    Task NotifyDashboardUpdatedAsync(CancellationToken cancellationToken);

    Task NotifyLoanAssignedAsync(
        Guid loanId,
        Guid userId,
        CancellationToken cancellationToken);
}

public sealed record ReceiveNotificationPayload(
    Guid Id,
    string Title,
    string Description,
    string? Link,
    DateTimeOffset Timestamp);

public sealed class NotificationHub(PresenceService presence) : Hub
{
    public const string Route = "/hubs/notifications";
    public const string ReceiveNotificationEvent = "ReceiveNotification";
    public const string DashboardUpdatedEvent = "DashboardUpdated";
    public const string LoanAssignedEvent = "LoanAssigned";

    public static string UserGroup(Guid userId) => $"user:{userId}";

    public static ReceiveNotificationPayload ToReceiveNotification(
        Guid id,
        string title,
        string body,
        string? link,
        DateTimeOffset createdAt) =>
        new(id, title, body, link, Timestamp: createdAt);

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
    TimeProvider timeProvider,
    ILogger<SignalRRealtimeNotifier> logger) : IRealtimeNotifier
{
    public async Task NotifyUserAsync(
        Guid notificationId,
        Guid userId,
        string title,
        string body,
        string? link,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var payload = NotificationHub.ToReceiveNotification(
            notificationId,
            title,
            body,
            link,
            createdAt);

        await hubContext.Clients.Group(NotificationHub.UserGroup(userId))
            .SendAsync(NotificationHub.ReceiveNotificationEvent, payload, cancellationToken);
    }

    public async Task NotifyDashboardUpdatedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await hubContext.Clients.All.SendAsync(
                NotificationHub.DashboardUpdatedEvent,
                new { Timestamp = timeProvider.GetUtcNow() },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DashboardUpdated broadcast failed");
        }
    }

    public async Task NotifyLoanAssignedAsync(
        Guid loanId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await hubContext.Clients.Group(NotificationHub.UserGroup(userId))
            .SendAsync(
                NotificationHub.LoanAssignedEvent,
                new { loanId, userId, escalated = false },
                cancellationToken);
    }
}

public static class NotificationHubModule
{
    public static void MapNotificationHub(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHub<NotificationHub>(NotificationHub.Route).RequireAuthorization();
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EBI.ALAS.Api.Features.Notifications;

/// <summary>
/// WebSocket hub for real-time notification delivery. Clients connect
/// with a valid JWT (the <c>[Authorize]</c> attribute rejects anonymous
/// handshakes) and receive <c>ReceiveNotification</c> events pushed
/// from the server.
///
/// Group routing:
///   • <c>Branch_{branchId}</c> — every officer in the same branch
///     receives branch-scoped events (new submissions, status changes).
///   • <c>All_Users</c> — system-wide broadcasts (workflow setting
///     changes, maintenance windows).
///
/// Individual user routing uses <see cref="Infrastructure.SignalR.JwtUserIdProvider"/>
/// so <c>IHubContext.Clients.User(id)</c> targets the correct connection
/// without manual group management.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var branchId = Context.User?.FindFirst("branchId")?.Value;
        if (!string.IsNullOrEmpty(branchId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Branch_{branchId}");
        }

        // System-wide broadcasts (workflow setting changes, etc.)
        await Groups.AddToGroupAsync(Context.ConnectionId, "All_Users");

        await base.OnConnectedAsync();
    }
}

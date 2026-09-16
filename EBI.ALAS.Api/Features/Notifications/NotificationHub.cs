using EBI.ALAS.Api.Features.ApprovalMatrix;
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
///   • <c>Approvers</c> — all approvers receive presence and assignment events.
///
/// Individual user routing uses <see cref="Infrastructure.SignalR.JwtUserIdProvider"/>
/// so <c>IHubContext.Clients.User(id)</c> targets the correct connection
/// without manual group management.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    private readonly IPresenceService _presence;
    private readonly ILoanAssignmentService _assignment;
    private readonly IHubContext<NotificationHub> _hub;

    public NotificationHub(
        IPresenceService presence,
        ILoanAssignmentService assignment,
        IHubContext<NotificationHub> hub)
    {
        _presence = presence;
        _assignment = assignment;
        _hub = hub;
    }

    public override async Task OnConnectedAsync()
    {
        var userIdStr = Context.User?.FindFirst("userId")?.Value;
        if (int.TryParse(userIdStr, out var userId))
        {
            _presence.SetOnline(userId, Context.ConnectionId);

            // Broadcast presence change to all approvers
            await _hub.Clients.Group("Approvers").SendAsync("PresenceChanged", new PresenceSnapshot(
                userId,
                _presence.IsOnline(userId),
                await _assignment.IsReviewingAsync(userId)));

            // Add to approvers group if the user is an approver
            var role = Context.User?.FindFirst("role")?.Value;
            if (role == "Approver")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "Approvers");
                // Newly online approver absorbs waiting work
                await _assignment.TryAssignPendingForAsync(userId);
            }
        }

        var branchId = Context.User?.FindFirst("branchId")?.Value;
        if (!string.IsNullOrEmpty(branchId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Branch_{branchId}");
        }

        // System-wide broadcasts (workflow setting changes, etc.)
        await Groups.AddToGroupAsync(Context.ConnectionId, "All_Users");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userIdStr = Context.User?.FindFirst("userId")?.Value;
        if (int.TryParse(userIdStr, out var userId))
        {
            _presence.SetOffline(userId, Context.ConnectionId);

            // Broadcast presence change to all approvers
            await _hub.Clients.Group("Approvers").SendAsync("PresenceChanged", new PresenceSnapshot(
                userId,
                _presence.IsOnline(userId),
                await _assignment.IsReviewingAsync(userId)));
        }

        await base.OnDisconnectedAsync(exception);
    }
}

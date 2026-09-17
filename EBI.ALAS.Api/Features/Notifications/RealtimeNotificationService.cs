using Microsoft.AspNetCore.SignalR;

namespace EBI.ALAS.Api.Features.Notifications;

/// <summary>
/// SignalR-backed implementation of <see cref="IRealtimeNotificationService"/>.
/// Uses <c>IHubContext&lt;NotificationHub&gt;</c> to push events to connected
/// clients without requiring an active hub method call.
///
/// Thread-safety: <c>IHubContext</c> is a singleton-safe service; the hub
/// connection lifecycle is managed by SignalR's internal pool.
/// </summary>
public class RealtimeNotificationService : IRealtimeNotificationService
{
    private readonly IHubContext<NotificationHub> _hubContext;

    public RealtimeNotificationService(IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyUserAsync(int userId, string title, string description, string? link)
    {
        await _hubContext.Clients.User(userId.ToString()).SendAsync("ReceiveNotification", new
        {
            Title = title,
            Description = description,
            Link = link,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task NotifyBranchAsync(string branchId, string title, string description, string? link)
    {
        await _hubContext.Clients.Group($"Branch_{branchId}").SendAsync("ReceiveNotification", new
        {
            Title = title,
            Description = description,
            Link = link,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task NotifyAllAsync(string title, string description, string? link)
    {
        await _hubContext.Clients.Group("All_Users").SendAsync("ReceiveNotification", new
        {
            Title = title,
            Description = description,
            Link = link,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task NotifyDashboardUpdateAsync(string? branchCode)
    {
        // When a loan status changes, the dashboard KPIs, pending queue,
        // now-serving list, and charts all shift. Rather than pushing the
        // full payload (expensive, per-branch cache keys), we send a
        // lightweight "DashboardUpdated" event that tells the client to
        // invalidate its TanStack Query cache and re-fetch.
        //
        // Routing:
        //   • If branchCode is set → push to Branch_{id} group only.
        //   • If null/empty → broadcast to All_Users (admin-level change).
        var target = string.IsNullOrEmpty(branchCode)
            ? _hubContext.Clients.Group("All_Users")
            : _hubContext.Clients.Group($"Branch_{branchCode}");

        await target.SendAsync("DashboardUpdated", new
        {
            Timestamp = DateTime.UtcNow
        });
    }
}

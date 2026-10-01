using Microsoft.AspNetCore.SignalR;
namespace EBI.ALAS.Api.Features.Notifications;
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
        var target = string.IsNullOrEmpty(branchCode)
            ? _hubContext.Clients.Group("All_Users")
            : _hubContext.Clients.Group($"Branch_{branchCode}");
        await target.SendAsync("DashboardUpdated", new
        {
            Timestamp = DateTime.UtcNow
        });
    }
}

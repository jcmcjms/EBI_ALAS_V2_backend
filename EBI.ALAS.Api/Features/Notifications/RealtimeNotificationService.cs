using EBI.ALAS.Api.Features.Presence;
using Microsoft.AspNetCore.SignalR;
namespace EBI.ALAS.Api.Features.Notifications;
public class RealtimeNotificationService : IRealtimeNotificationService
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly IEntityWatchService _watch;
    public RealtimeNotificationService(IHubContext<NotificationHub> hubContext, IEntityWatchService watch)
    {
        _hubContext = hubContext;
        _watch = watch;
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
    public async Task NotifyEntityWatchersAsync(
        int loanId,
        string title,
        string description,
        string? link,
        IReadOnlyCollection<int> excludeUserIds)
    {
        var key = new EntityWatchKey("LoanApplication", loanId);
        var targets = _watch.Viewers(key)
            .Select(v => v.UserId)
            .Where(id => !excludeUserIds.Contains(id))
            .Select(id => id.ToString())
            .ToList();
        if (targets.Count == 0) return;
        await _hubContext.Clients.Users(targets).SendAsync("ReceiveNotification", new
        {
            Title = title,
            Description = description,
            Link = link,
            Timestamp = DateTime.UtcNow
        });
    }
}

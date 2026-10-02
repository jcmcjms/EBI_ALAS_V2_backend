namespace EBI.ALAS.Api.Features.Notifications;
public interface IRealtimeNotificationService
{
    Task NotifyUserAsync(int userId, string title, string description, string? link);
    Task NotifyBranchAsync(string branchId, string title, string description, string? link);
    Task NotifyAllAsync(string title, string description, string? link);
    Task NotifyDashboardUpdateAsync(string? branchCode);
    Task NotifyEntityWatchersAsync(
        int loanId,
        string title,
        string description,
        string? link,
        IReadOnlyCollection<int> excludeUserIds);
}

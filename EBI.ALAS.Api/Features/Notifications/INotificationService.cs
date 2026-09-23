namespace EBI.ALAS.Api.Features.Notifications;

/// <summary>
/// Persistence-layer API for per-user notifications.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Insert a new notification for the given user.
    /// </summary>
    Task CreateAsync(int userId, string title, string description, string? link = null);

    /// <summary>
    /// Batch insert multiple notifications in a single SaveChanges call.
    /// Use this instead of calling CreateAsync in a loop (N+1 writes).
    /// </summary>
    Task CreateBatchAsync(IEnumerable<(int UserId, string Title, string Description, string? Link)> notifications);

    /// <summary>
    /// Most-recent notifications (default 20) for the given user, newest first.
    /// </summary>
    Task<List<NotificationResponse>> GetUserNotificationsAsync(int userId, int limit = 20);
}

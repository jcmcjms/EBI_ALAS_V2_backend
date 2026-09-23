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
    /// Batch insert with explicit notification types. Callers that care about
    /// type pass it in the draft; callers that don't get classifier defaults.
    /// </summary>
    Task CreateBatchAsync(IEnumerable<NotificationDraft> drafts);

    /// <summary>
    /// Most-recent notifications (default 20) for the given user, newest first.
    /// </summary>
    Task<List<NotificationResponse>> GetUserNotificationsAsync(int userId, int limit = 20);

    /// <summary>
    /// Server-driven paged inbox with status/type/search filters.
    /// Returns items, totalCount, and unreadCount in one round-trip.
    /// </summary>
    Task<InboxPage> GetInboxAsync(int userId, InboxQuery query, CancellationToken ct = default);

    /// <summary>
    /// Mark a single notification as read. Idempotent — re-reading keeps the original ReadAt.
    /// Returns false if the notification doesn't exist or doesn't belong to the user.
    /// </summary>
    Task<bool> MarkReadAsync(int userId, int notificationId, CancellationToken ct = default);

    /// <summary>
    /// Mark all unread notifications as read for the given user.
    /// Returns the number of rows changed.
    /// </summary>
    Task<int> MarkAllReadAsync(int userId, CancellationToken ct = default);
}

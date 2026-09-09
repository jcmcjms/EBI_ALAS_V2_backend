namespace EBI.ALAS.Api.Features.Notifications;

/// <summary>
/// Persistence-layer API for per-user notifications. Workflow handlers
/// (loan submission, status changes) call <see cref="CreateAsync"/> at the
/// end of their happy path; the SPA's header bell calls
/// <see cref="GetUserNotificationsAsync"/> on a poll interval.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Insert a new notification for <paramref name="userId"/>. Fire-and-forget
    /// from the caller's perspective — SaveChangesAsync commits immediately so
    /// the next reader (including the SPA's poll) sees the row.
    /// </summary>
    Task CreateAsync(int userId, string title, string description, string? link = null);

    /// <summary>
    /// Most-recent <paramref name="limit"/> notifications (default 20) for
    /// the given user, newest first. Used by the header bell.
    /// </summary>
    Task<List<NotificationResponse>> GetUserNotificationsAsync(int userId, int limit = 20);
}
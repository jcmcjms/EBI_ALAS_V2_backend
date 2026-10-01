namespace EBI.ALAS.Api.Features.Notifications;
public interface INotificationService
{
    Task CreateAsync(int userId, string title, string description, string? link = null);
    Task CreateBatchAsync(IEnumerable<(int UserId, string Title, string Description, string? Link)> notifications);
    Task CreateBatchAsync(IEnumerable<NotificationDraft> drafts);
    Task<List<NotificationResponse>> GetUserNotificationsAsync(int userId, int limit = 20);
    Task<InboxPage> GetInboxAsync(int userId, InboxQuery query, CancellationToken ct = default);
    Task<bool> MarkReadAsync(int userId, int notificationId, CancellationToken ct = default);
    Task<int> MarkAllReadAsync(int userId, CancellationToken ct = default);
    void TrackCreate(int userId, string title, string description, string? link = null);
}

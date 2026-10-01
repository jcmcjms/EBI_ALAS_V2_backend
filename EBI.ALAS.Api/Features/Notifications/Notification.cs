using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Features.Auth;
namespace EBI.ALAS.Api.Features.Notifications;
public sealed class Notification
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; init; }
    public string Type { get; set; } = NotificationTypes.System;
    public DateTime? ReadAt { get; set; }
    public User? User { get; set; }
}
public sealed record NotificationResponse(
    int Id,
    string Title,
    string Description,
    string? Link,
    bool IsRead,
    DateTime CreatedAt,
    string Type,
    DateTime? ReadAt);
public sealed record InboxPage(
    IReadOnlyList<NotificationResponse> Items,
    int TotalCount,
    int UnreadCount);
public sealed record InboxQuery(
    int Page,
    int PageSize,
    string Status,
    string? Type,
    string? Search);
public sealed record MarkAllReadResponse(int ChangedCount);
public sealed record NotificationDraft(
    int UserId,
    string Title,
    string Description,
    string? Link,
    string? Type = null);

using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Features.Notifications;

/// <summary>
/// Persistent, per-user notification row. Inserted whenever a workflow event
/// happens that the recipient needs to know about.
/// EF Core entity — uses init setters for immutability after construction.
/// </summary>
public sealed class Notification
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// UI bucket the inbox filters on. Written at creation time so
    /// filtering/paging happens in SQL, not by keyword-guessing titles client-side.
    /// </summary>
    public string Type { get; set; } = NotificationTypes.System;

    /// <summary>
    /// When the owner marked it read; null = unread. Audit-friendly.
    /// </summary>
    public DateTime? ReadAt { get; set; }

    public User? User { get; set; }
}

/// <summary>
/// Wire format for GET /api/notifications. Immutable record.
/// </summary>
public sealed record NotificationResponse(
    int Id,
    string Title,
    string Description,
    string? Link,
    bool IsRead,
    DateTime CreatedAt,
    string Type,
    DateTime? ReadAt);

/// <summary>
/// Paged inbox envelope — unreadCount rides along so bell badge and
/// page header never need a second round-trip.
/// </summary>
public sealed record InboxPage(
    IReadOnlyList<NotificationResponse> Items,
    int TotalCount,
    int UnreadCount);

/// <summary>
/// Query parameters for the server-driven inbox endpoint.
/// </summary>
public sealed record InboxQuery(
    int Page,
    int PageSize,
    string Status,
    string? Type,
    string? Search);

/// <summary>
/// Response for mark-all-read and mark-read endpoints.
/// </summary>
public sealed record MarkAllReadResponse(int ChangedCount);

/// <summary>
/// Creation unit carrying an explicit type; callers that don't care
/// keep using the 4-tuple overload and get classifier defaults.
/// </summary>
public sealed record NotificationDraft(
    int UserId,
    string Title,
    string Description,
    string? Link,
    string? Type = null);

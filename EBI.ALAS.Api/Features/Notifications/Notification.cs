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
    DateTime CreatedAt);

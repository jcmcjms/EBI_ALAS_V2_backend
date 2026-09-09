using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Features.Notifications;

/// <summary>
/// Persistent, per-user notification row. Inserted whenever a workflow event
/// happens that the recipient needs to know about (loan submitted, status
/// changed, returned for revision). Surfaced via the header bell on the
/// frontend and persisted in <c>Notifications</c> table.
/// </summary>
public class Notification
{
    public int Id { get; set; }

    /// <summary>
    /// Recipient's <c>User.Id</c>. The header bell query filters on this
    /// column via an index — see <see cref="Infrastructure.Data.AppDbContext"/>.
    /// </summary>
    public int UserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Optional SPA route — the FE uses it to deep-link from the bell
    /// straight to the loan monitoring page for the affected application.
    /// </summary>
    public string? Link { get; set; }

    public bool IsRead { get; set; } = false;

    public DateTime CreatedAt { get; set; }

    // ─── Navigation Property ───────────────────────────────────────────────
    public User? User { get; set; }
}

/// <summary>
/// Wire format for GET /api/notifications. A record so the JSON
/// serializer emits the camelCased fields the FE expects
/// (id, title, description, link, isRead, createdAt).
/// </summary>
public record NotificationResponse(
    int Id,
    string Title,
    string Description,
    string? Link,
    bool IsRead,
    DateTime CreatedAt);
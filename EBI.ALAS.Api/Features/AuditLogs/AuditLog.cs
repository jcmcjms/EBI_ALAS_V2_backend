using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Features.AuditLogs;

/// <summary>
/// System-wide audit log entity that records all user actions, system events,
/// and data modifications for compliance and forensic analysis.
/// </summary>
public class AuditLog
{
    public int Id { get; set; }

    /// <summary>UTC timestamp of when the event occurred.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>ID of the user who performed the action. Null for system-initiated events.</summary>
    public int? UserId { get; set; }

    /// <summary>Display name of the actor (denormalized for audit readability).</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// The type of action performed.
    /// Values: Create, Update, StatusChange, Login, Logout, Delete
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// The type of entity affected.
    /// Values: LoanApplication, User, Auth, Branch, Role, LoanProduct
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Primary key or identifier of the affected entity.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable label for the affected entity (e.g., "LA-2026-08-9942 (Juan Cruz)").
    /// Used for display in the audit log UI.
    /// </summary>
    public string EntityLabel { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable, immutable sentence describing what happened.
    /// This is the primary summary shown to business users (e.g., "Approved loan and routed to disbursement").
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Exact technical JSON diff of the entity state change.
    /// Hidden behind a "Technical Details" toggle in the UI for forensic auditors.
    /// Null for non-entity actions like Login/Logout.
    /// </summary>
    public string? RawChanges { get; set; }

    /// <summary>IP address of the client that initiated the action.</summary>
    public string? IpAddress { get; set; }

    /// <summary>User-Agent header of the client browser/application.</summary>
    public string? UserAgent { get; set; }

    // ─── Navigation Property ────────────────────────────────────────────────────
    public User? User { get; set; }
}

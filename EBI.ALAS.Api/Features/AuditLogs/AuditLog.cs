using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Features.AuditLogs;

/// <summary>
/// Audit log entity. Records all state-changing operations for compliance.
/// EF Core entity — uses init setters for immutability after construction.
/// </summary>
public sealed class AuditLog
{
    public int Id { get; init; }
    public DateTime Timestamp { get; init; }
    public int? UserId { get; init; }
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string EntityLabel { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? RawChanges { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    // Navigation Property
    public User? User { get; set; }
}

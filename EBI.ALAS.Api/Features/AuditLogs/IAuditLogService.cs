namespace EBI.ALAS.Api.Features.AuditLogs;

/// <summary>
/// Interface for recording audit log entries.
/// Implemented by AuditLogService and used throughout the application
/// to capture all significant user actions and system events.
/// </summary>
public interface IAuditLogService
{
    /// <summary>Records an audit log entry.</summary>
    Task LogAsync(
        int? userId,
        string userName,
        string action,
        string entityType,
        string entityId,
        string entityLabel,
        string summary,
        string? rawChanges = null,
        string? ipAddress = null,
        string? userAgent = null);

    /// <summary>Records a login event.</summary>
    Task LogLoginAsync(int userId, string userName, string ipAddress, string? userAgent);

    /// <summary>Records a logout event.</summary>
    Task LogLogoutAsync(int userId, string userName, string ipAddress, string? userAgent);
}

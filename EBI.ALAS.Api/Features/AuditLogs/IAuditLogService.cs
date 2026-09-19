namespace EBI.ALAS.Api.Features.AuditLogs;

/// <summary>
/// Audit log service interface. Records state-changing operations for compliance.
/// </summary>
public interface IAuditLogService
{
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
}

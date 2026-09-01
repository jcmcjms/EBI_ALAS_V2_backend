namespace EBI.ALAS.Api.Features.AuditLogs;
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

    Task LogLoginAsync(int userId, string userName, string ipAddress, string? userAgent);

    Task LogLogoutAsync(int userId, string userName, string ipAddress, string? userAgent);
}

using Ebi.Alas.Api.Infrastructure.Persistence;

namespace Ebi.Alas.Api.Features.AuditLogs;

public interface IAuditLogService
{
    Task LogAsync(
        Guid? userId,
        string userName,
        string action,
        string entityType,
        string entityId,
        string entityLabel,
        string summary,
        string? rawChanges = null,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default);
}

public sealed class AuditLogService(AlasDbContext db, TimeProvider timeProvider) : IAuditLogService
{
    public async Task LogAsync(
        Guid? userId,
        string userName,
        string action,
        string entityType,
        string entityId,
        string entityLabel,
        string summary,
        string? rawChanges = null,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        var entry = AuditLog.Record(
            userId,
            userName,
            action,
            entityType,
            entityId,
            entityLabel,
            summary,
            rawChanges,
            ipAddress,
            userAgent,
            timeProvider.GetUtcNow());
        db.AuditLogs.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }
}

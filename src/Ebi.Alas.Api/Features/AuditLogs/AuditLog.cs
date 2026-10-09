namespace Ebi.Alas.Api.Features.AuditLogs;

public sealed class AuditLog
{
    private AuditLog()
    {
        UserName = string.Empty;
        Action = string.Empty;
        EntityType = string.Empty;
        EntityId = string.Empty;
        EntityLabel = string.Empty;
        Summary = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid? UserId { get; private set; }

    public string UserName { get; private set; }

    public string Action { get; private set; }

    public string EntityType { get; private set; }

    public string EntityId { get; private set; }

    public string EntityLabel { get; private set; }

    public string Summary { get; private set; }

    public string? RawChanges { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public DateTimeOffset Timestamp { get; private set; }

    public static AuditLog Record(
        Guid? userId,
        string userName,
        string action,
        string entityType,
        string entityId,
        string entityLabel,
        string summary,
        string? rawChanges,
        string? ipAddress,
        string? userAgent,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        return new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            UserName = userName?.Trim() ?? string.Empty,
            Action = action.Trim(),
            EntityType = entityType.Trim(),
            EntityId = entityId?.Trim() ?? string.Empty,
            EntityLabel = entityLabel?.Trim() ?? string.Empty,
            Summary = summary?.Trim() ?? string.Empty,
            RawChanges = rawChanges,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Timestamp = now
        };
    }
}

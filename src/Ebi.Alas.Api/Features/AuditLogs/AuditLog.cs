namespace Ebi.Alas.Api.Features.AuditLogs;

public sealed class AuditLog
{
    private AuditLog()
    {
        Action = string.Empty;
        EntityType = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid? UserId { get; private set; }

    public string Action { get; private set; }

    public string EntityType { get; private set; }

    public string? EntityId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static AuditLog Record(
        Guid? userId,
        string action,
        string entityType,
        string? entityId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        return new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Action = action.Trim(),
            EntityType = entityType.Trim(),
            EntityId = entityId,
            CreatedAt = now
        };
    }
}

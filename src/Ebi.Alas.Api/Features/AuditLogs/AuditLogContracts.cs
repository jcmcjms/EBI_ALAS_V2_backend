namespace Ebi.Alas.Api.Features.AuditLogs;

public sealed record AuditLogResponse(
    Guid Id,
    Guid? UserId,
    string Action,
    string EntityType,
    string? EntityId,
    DateTimeOffset CreatedAt);

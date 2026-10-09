namespace Ebi.Alas.Api.Features.AuditLogs;

public sealed record AuditLogResponse(
    Guid Id,
    DateTimeOffset Timestamp,
    Guid? UserId,
    string UserName,
    string Action,
    string EntityType,
    string EntityId,
    string EntityLabel,
    string Summary,
    string? RawChanges,
    string? IpAddress,
    string? UserAgent);

public sealed record AuditLogQuery(
    int Page,
    int PageSize,
    string? Search,
    string? Action,
    string? EntityType,
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate);

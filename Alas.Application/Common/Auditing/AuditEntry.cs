namespace Alas.Application.Common.Auditing;

public sealed record AuditEntry(
    string Action,
    bool IsSuccess,
    Guid? UserId,
    string? Username,
    string? EntityType,
    string? EntityId,
    string? Details,
    string? FailureReason,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId);
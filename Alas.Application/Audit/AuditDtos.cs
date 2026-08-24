namespace Alas.Application.Audit;

public sealed record AuditLogDto(
    Guid Id,
    DateTimeOffset UtcTimestamp,
    string Action,
    bool IsSuccess,
    Guid? UserId,
    string? Username,
    string? EntityType,
    string? EntityId,
    string? Details,
    string? FailureReason,
    string? IpAddress,
    string? CorrelationId);

public sealed record AuditQueryParams
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string? Action { get; init; }
    public string? Username { get; init; }
    public Guid? UserId { get; init; }
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public bool? IsSuccess { get; init; }
}

public sealed record AuditPagedResult(
    IReadOnlyCollection<AuditLogDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

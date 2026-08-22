namespace Alas.Infrastructure.Auditing;

public class AuditLog
{
    public Guid Id { get; set; }
    public DateTimeOffset UtcTimestamp { get; set; }
    public string Action { get; set; }
    public bool IsSuccess { get; set; }
    public Guid? UserId { get; set; }
    public string? Username { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public string? FailureReason { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
}
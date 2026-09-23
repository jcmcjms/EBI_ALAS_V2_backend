namespace EBI.ALAS.Api.Infrastructure.Messaging.Events;

/// <summary>
/// Event published when a loan status changes. Consumed by notification
/// and audit log consumers for async processing — unblocks the HTTP response.
/// </summary>
public sealed record LoanStatusChangedEvent(
    int LoanId,
    string LamId,
    string ClientName,
    string BranchCode,
    string FromStatus,
    string ToStatus,
    int ActorUserId,
    string ActorName,
    string? Comments,
    string? Verdict,
    DateTime OccurredAt);

/// <summary>
/// Event published when a notification needs to be sent.
/// Decouples notification creation from the loan workflow.
/// </summary>
public sealed record NotificationCreatedEvent(
    int UserId,
    string Title,
    string Description,
    string? Link,
    DateTime OccurredAt,
    string? Type = null);

/// <summary>
/// Event published when an audit log entry needs to be recorded.
/// Ensures audit logging doesn't block the response path.
/// </summary>
public sealed record AuditLogRecordedEvent(
    int? UserId,
    string UserName,
    string Action,
    string EntityType,
    string EntityId,
    string EntityLabel,
    string Summary,
    string? RawChanges,
    string? IpAddress,
    string? UserAgent,
    DateTime OccurredAt);

/// <summary>
/// Event published when a dashboard needs real-time refresh.
/// Pushed to SignalR via the consumer.
/// </summary>
public sealed record DashboardRefreshEvent(
    string BranchCode,
    DateTime OccurredAt);

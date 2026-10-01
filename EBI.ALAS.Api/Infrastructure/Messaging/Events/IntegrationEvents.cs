namespace EBI.ALAS.Api.Infrastructure.Messaging.Events;
public sealed record LoanStatusChangedEvent(
    int LoanId,
    string LamId,
    string ClientName,
    string BranchCode,
    string FromStatus,
    string ToStatus,
    int ActorUserId,
    string ActorName,
    string UserRole,
    int LoanCreatedById,
    string? Comments,
    string? Verdict,
    string ActionName,
    DateTime OccurredAt);
public sealed record NotificationCreatedEvent(
    int UserId,
    string Title,
    string Description,
    string? Link,
    DateTime OccurredAt,
    string? Type = null);
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
public sealed record DashboardRefreshEvent(
    string BranchCode,
    DateTime OccurredAt);

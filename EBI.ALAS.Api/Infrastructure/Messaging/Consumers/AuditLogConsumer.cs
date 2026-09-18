using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Infrastructure.Messaging.Events;
using MassTransit;

namespace EBI.ALAS.Api.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumes audit log events and persists them to the database.
/// This decouples audit logging from the HTTP response path,
/// ensuring loan status updates return immediately.
/// </summary>
public sealed class AuditLogConsumer : IConsumer<AuditLogRecordedEvent>
{
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<AuditLogConsumer> _logger;

    public AuditLogConsumer(IAuditLogger auditLogger, ILogger<AuditLogConsumer> logger)
    {
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AuditLogRecordedEvent> context)
    {
        var msg = context.Message;

        _logger.LogInformation(
            "Processing audit log: {Action} on {EntityType}/{EntityId} by {UserName}",
            msg.Action, msg.EntityType, msg.EntityId, msg.UserName);

        // The existing IAuditLogger only supports loan action logging.
        // For generic entity audit, we use the loan-specific overload
        // when the entity is a loan, otherwise log via the generic path.
        if (msg.EntityType == "LoanApplication" && int.TryParse(msg.EntityId, out var loanId) && msg.UserId.HasValue)
        {
            await _auditLogger.LogActionAsync(
                loanId,
                msg.UserId.Value,
                msg.Action,
                null, // fromStatus — already captured in Summary
                null, // toStatus — already captured in Summary
                msg.Summary);
        }
        else
        {
            _logger.LogWarning(
                "Audit log for non-loan entity {EntityType}/{EntityId} — " +
                "generic audit logging not yet implemented. Summary: {Summary}",
                msg.EntityType, msg.EntityId, msg.Summary);
        }
    }
}

using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Infrastructure.Messaging.Events;
using MassTransit;
namespace EBI.ALAS.Api.Infrastructure.Messaging.Consumers;
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
        if (msg.EntityType == "LoanApplication" && int.TryParse(msg.EntityId, out var loanId) && msg.UserId.HasValue)
        {
            await _auditLogger.LogActionAsync(
                loanId,
                msg.UserId.Value,
                msg.Action,
                null,
                null,
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

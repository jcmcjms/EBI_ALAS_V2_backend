using Alas.Application.Common.Auditing;

namespace Alas.Infrastructure.Auditing;

public sealed class ChannelAuditLogger: IAuditLogger
{
    private readonly AuditChannel _channel;

    public ChannelAuditLogger(AuditChannel channel)
    {
        _channel = channel;
    }

    public async Task LogAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            UtcTimestamp = DateTimeOffset.UtcNow,
            Action = entry.Action,
            IsSuccess = entry.IsSuccess,
            UserId = entry.UserId,
            Username = entry.Username,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Details = entry.Details,
            FailureReason = entry.FailureReason,
            IpAddress = entry.IpAddress,
            UserAgent = entry.UserAgent,
            CorrelationId = entry.CorrelationId
        };
        await _channel.Writer.WriteAsync(auditLog, cancellationToken);
    }
}
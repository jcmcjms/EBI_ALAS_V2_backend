using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Infrastructure.Data;

namespace EBI.ALAS.Api.Features.AuditLogs;
public class AuditLogService : IAuditLogService
{
    private readonly AppDbContext _context;
    private readonly ITimeProvider _timeProvider;

    public AuditLogService(AppDbContext context, ITimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task LogAsync(
        int? userId,
        string userName,
        string action,
        string entityType,
        string entityId,
        string entityLabel,
        string summary,
        string? rawChanges = null,
        string? ipAddress = null,
        string? userAgent = null)
    {
        var entry = new AuditLog
        {
            Timestamp = _timeProvider.UtcNow,
            UserId = userId,
            UserName = userName,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            EntityLabel = entityLabel,
            Summary = summary,
            RawChanges = rawChanges,
            IpAddress = ipAddress,
            UserAgent = userAgent
        };

        _context.AuditLogs.Add(entry);
        await _context.SaveChangesAsync();
    }
}

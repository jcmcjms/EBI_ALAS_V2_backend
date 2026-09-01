using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Infrastructure.Data;

namespace EBI.ALAS.Api.Features.AuditLogs;

/// <summary>
/// Service responsible for persisting audit log entries to the database.
/// All significant user actions and system events should flow through here.
/// </summary>
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

    public async Task LogLoginAsync(int userId, string userName, string ipAddress, string? userAgent)
    {
        await LogAsync(
            userId,
            userName,
            action: "Login",
            entityType: "Auth",
            entityId: $"user_{userId}",
            entityLabel: userName,
            summary: $"User '{userName}' logged in successfully.",
            ipAddress: ipAddress,
            userAgent: userAgent);
    }

    public async Task LogLogoutAsync(int userId, string userName, string ipAddress, string? userAgent)
    {
        await LogAsync(
            userId,
            userName,
            action: "Logout",
            entityType: "Auth",
            entityId: $"user_{userId}",
            entityLabel: userName,
            summary: $"User '{userName}' logged out.",
            ipAddress: ipAddress,
            userAgent: userAgent);
    }
}

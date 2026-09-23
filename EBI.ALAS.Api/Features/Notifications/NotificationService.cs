using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Notifications;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _context;
    private readonly ITimeProvider _timeProvider;

    public NotificationService(AppDbContext context, ITimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task CreateAsync(int userId, string title, string description, string? link = null)
    {
        var notification = new Notification
        {
            UserId = userId,
            Title = title,
            Description = description,
            Link = link,
            CreatedAt = _timeProvider.UtcNow
        };
        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task CreateBatchAsync(
        IEnumerable<(int UserId, string Title, string Description, string? Link)> notifications)
    {
        // Single AddRange + single SaveChanges instead of N individual inserts.
        // This reduces N round trips to 1 and is critical for status-change endpoints
        // that notify multiple recipients (approvers, evaluators, etc.).
        var now = _timeProvider.UtcNow;
        var entities = notifications.Select(n => new Notification
        {
            UserId = n.UserId,
            Title = n.Title,
            Description = n.Description,
            Link = n.Link,
            CreatedAt = now
        }).ToList();

        if (entities.Count == 0) return;

        _context.Notifications.AddRange(entities);
        await _context.SaveChangesAsync();
    }

    public async Task<List<NotificationResponse>> GetUserNotificationsAsync(int userId, int limit = 20)
    {
        return await _context.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new NotificationResponse(
                n.Id,
                n.Title,
                n.Description,
                n.Link,
                n.IsRead,
                n.CreatedAt))
            .ToListAsync();
    }
}
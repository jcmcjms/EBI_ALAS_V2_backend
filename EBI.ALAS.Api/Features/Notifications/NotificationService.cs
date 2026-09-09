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
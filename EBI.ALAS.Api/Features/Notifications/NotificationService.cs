using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Shared.Time;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace EBI.ALAS.Api.Features.Notifications;
public static class NotificationClassifier
{
    public static string Classify(string title)
    {
        var t = title.ToLowerInvariant();
        if (t.Contains("ready for") || t.Contains("recommendation") || t.Contains("approval"))
            return NotificationTypes.Action;
        if (t.Contains("submitted") || t.Contains("application") || t.Contains("returned"))
            return NotificationTypes.Application;
        if (t.Contains("status update"))
            return NotificationTypes.Message;
        return NotificationTypes.System;
    }
}
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
            CreatedAt = _timeProvider.UtcNow,
            Type = NotificationClassifier.Classify(title)
        };
        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();
    }
    public void TrackCreate(int userId, string title, string description, string? link = null)
    {
        var notification = new Notification
        {
            UserId = userId,
            Title = title,
            Description = description,
            Link = link,
            CreatedAt = _timeProvider.UtcNow,
            Type = NotificationClassifier.Classify(title)
        };
        _context.Notifications.Add(notification);
    }
    public async Task CreateBatchAsync(
        IEnumerable<(int UserId, string Title, string Description, string? Link)> notifications)
    {
        var drafts = notifications.Select(n =>
            new NotificationDraft(n.UserId, n.Title, n.Description, n.Link, Type: null));
        await CreateBatchAsync(drafts);
    }
    public async Task CreateBatchAsync(IEnumerable<NotificationDraft> drafts)
    {
        var now = _timeProvider.UtcNow;
        var entities = drafts.Select(d => new Notification
        {
            UserId = d.UserId,
            Title = d.Title,
            Description = d.Description,
            Link = d.Link,
            CreatedAt = now,
            Type = d.Type ?? NotificationClassifier.Classify(d.Title)
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
                n.CreatedAt,
                n.Type,
                n.ReadAt))
            .ToListAsync();
    }
    public async Task<InboxPage> GetInboxAsync(int userId, InboxQuery query, CancellationToken ct = default)
    {
        var q = _context.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        q = query.Status switch
        {
            "unread" => q.Where(n => !n.IsRead),
            "read" => q.Where(n => n.IsRead),
            _ => q,
        };
        if (!string.IsNullOrWhiteSpace(query.Type))
            q = q.Where(n => n.Type == query.Type);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = EscapeLike(query.Search.Trim());
            q = q.Where(n =>
                EF.Functions.Like(n.Title, $"%{s}%") ||
                EF.Functions.Like(n.Description, $"%{s}%"));
        }
        var unreadCount = await _context.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsRead)
            .CountAsync(ct);
        var totalCount = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(n => new NotificationResponse(
                n.Id, n.Title, n.Description, n.Link,
                n.IsRead, n.CreatedAt, n.Type, n.ReadAt))
            .ToListAsync(ct);
        return new InboxPage(items, totalCount, unreadCount);
    }
    public async Task<bool> MarkReadAsync(int userId, int notificationId, CancellationToken ct = default)
    {
        var row = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, ct);
        if (row is null) return false;
        if (!row.IsRead)
        {
            row.IsRead = true;
            row.ReadAt = _timeProvider.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
        return true;
    }
    public Task<int> MarkAllReadAsync(int userId, CancellationToken ct = default)
    {
        return _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, _timeProvider.UtcNow), ct);
    }
    private static string EscapeLike(string input) =>
        input.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}

using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Notifications;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/notifications", async (
            int? page,
            int? pageSize,
            string? status,
            string? type,
            string? search,
            AlasDbContext db,
            System.Security.Claims.ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var query = NotificationInboxFilter.Apply(
                db.Notifications.AsNoTracking().Where(n => n.UserId == caller.UserId),
                status,
                type,
                search,
                forceActionRequired: caller.IsEvaluator);

            var totalCount = await query.CountAsync(cancellationToken);
            var unreadCount = await db.Notifications.AsNoTracking()
                .CountAsync(n => n.UserId == caller.UserId && n.ReadAt == null, cancellationToken);

            var take = Math.Clamp(pageSize ?? 10, 1, 100);
            var skip = Math.Max(0, (Math.Max(page ?? 1, 1) - 1) * take);

            var items = await query
                .OrderByDescending(n => n.CreatedAt)
                .Skip(skip)
                .Take(take)
                .Select(n => new NotificationResponse(
                    n.Id,
                    n.Title,
                    n.Body,
                    n.Type.ToString(),
                    n.CreatedAt,
                    n.ReadAt,
                    n.Link))
                .ToListAsync(cancellationToken);

            return Results.Ok(new NotificationInboxPage(items, totalCount, unreadCount));
        })
        .RequireAuthorization()
        .WithTags("Notifications");

        endpoints.MapPost("/api/notifications/{id:guid}/read", async (
            Guid id,
            AlasDbContext db,
            System.Security.Claims.ClaimsPrincipal user,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var notification = await db.Notifications
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == caller.UserId, cancellationToken);
            if (notification is null)
            {
                return Results.NotFound();
            }

            notification.MarkRead(timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .RequireRateLimiting("write")
        .WithTags("Notifications");

        endpoints.MapPost("/api/notifications/read-all", async (
            AlasDbContext db,
            System.Security.Claims.ClaimsPrincipal user,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var changedCount = await NotificationReadAll.MarkOwnUnreadAsync(
                db,
                caller.UserId,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return Results.Ok(new { changedCount });
        })
        .RequireAuthorization()
        .RequireRateLimiting("write")
        .WithTags("Notifications");
    }
}

public static class NotificationInboxFilter
{
    public static IQueryable<Notification> Apply(
        IQueryable<Notification> query,
        string? status,
        string? type,
        string? search,
        bool forceActionRequired = false)
    {
        if (forceActionRequired)
        {
            query = query.Where(n => n.Type == NotificationType.Action);
        }
        else if (!string.IsNullOrWhiteSpace(type)
            && Enum.TryParse<NotificationType>(type.Trim(), ignoreCase: true, out var parsedType))
        {
            query = query.Where(n => n.Type == parsedType);
        }

        if (string.Equals(status, "unread", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(n => n.ReadAt == null);
        }
        else if (string.Equals(status, "read", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(n => n.ReadAt != null);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(n => n.Title.Contains(term) || n.Body.Contains(term));
        }

        return query;
    }
}

public static class NotificationReadAll
{
    public static async Task<int> MarkOwnUnreadAsync(
        AlasDbContext db,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var unread = await db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .Take(500)
            .ToListAsync(cancellationToken);
        foreach (var notification in unread)
        {
            notification.MarkRead(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return unread.Count;
    }
}

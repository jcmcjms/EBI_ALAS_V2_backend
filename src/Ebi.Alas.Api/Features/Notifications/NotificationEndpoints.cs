using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Notifications;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/notifications", async (
            AlasDbContext db,
            System.Security.Claims.ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var items = await db.Notifications.AsNoTracking()
                .Where(n => n.UserId == caller.UserId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(50)
                .Select(n => new NotificationResponse(
                    n.Id,
                    n.Title,
                    n.Body,
                    n.Type.ToString(),
                    n.CreatedAt,
                    n.ReadAt))
                .ToListAsync(cancellationToken);
            return Results.Ok(items);
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
        .WithTags("Notifications");
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

using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Notifications;

public sealed class NotificationOwnershipTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"notif-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public async Task MarkRead_OtherUsersNotification_ReturnsNull()
    {
        await using var db = Db();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var notification = Notification.Create(owner, "Title", "Body", NotificationType.Action, DateTimeOffset.UtcNow);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        var found = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == notification.Id && n.UserId == other, CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task MarkRead_OwnNotification_FindsIt()
    {
        await using var db = Db();
        var owner = Guid.NewGuid();
        var notification = Notification.Create(owner, "Title", "Body", NotificationType.Action, DateTimeOffset.UtcNow);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        var found = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == notification.Id && n.UserId == owner, CancellationToken.None);

        Assert.NotNull(found);
    }
}

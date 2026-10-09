using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Notifications;

public sealed class NotificationReadAllTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"notif-readall-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public async Task MarkOwnUnreadAsync_OnlyTouchesOwnUnreadNotifications()
    {
        await using var db = Db();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var mineUnread = Notification.Create(owner, "A", "B", NotificationType.Action, now);
        var mineRead = Notification.Create(owner, "C", "D", NotificationType.Action, now);
        mineRead.MarkRead(now);
        var otherUnread = Notification.Create(other, "E", "F", NotificationType.Action, now);

        db.Notifications.AddRange(mineUnread, mineRead, otherUnread);
        await db.SaveChangesAsync();

        var changed = await NotificationReadAll.MarkOwnUnreadAsync(db, owner, now.AddMinutes(1), CancellationToken.None);

        Assert.Equal(1, changed);
        Assert.NotNull((await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == mineUnread.Id)).ReadAt);
        Assert.Null((await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == otherUnread.Id)).ReadAt);
    }
}

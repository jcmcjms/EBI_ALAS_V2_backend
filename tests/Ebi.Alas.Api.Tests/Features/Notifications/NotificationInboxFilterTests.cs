using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Notifications;

public sealed class NotificationInboxFilterTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"notif-filter-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static async Task SeedAsync(AlasDbContext db, Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        db.Notifications.AddRange(
            Notification.Create(userId, "Loan submitted", "Ready for recommendation", NotificationType.Application, now.AddMinutes(-3)),
            Notification.Create(userId, "Approve now", "Action required on PN-9", NotificationType.Action, now.AddMinutes(-2)),
            Notification.Create(userId, "System notice", "Password policy", NotificationType.System, now.AddMinutes(-1)));
        await db.SaveChangesAsync();

        var read = await db.Notifications.FirstAsync(n => n.Title == "Loan submitted");
        read.MarkRead(now);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task StatusUnread_ExcludesRead()
    {
        await using var db = Db();
        var userId = Guid.NewGuid();
        await SeedAsync(db, userId);

        var rows = await NotificationInboxFilter
            .Apply(db.Notifications.Where(n => n.UserId == userId), "unread", null, null)
            .Select(n => n.Title)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain("Loan submitted", rows);
    }

    [Fact]
    public async Task Type_IsCaseInsensitive()
    {
        await using var db = Db();
        var userId = Guid.NewGuid();
        await SeedAsync(db, userId);

        var rows = await NotificationInboxFilter
            .Apply(db.Notifications.Where(n => n.UserId == userId), null, "application", null)
            .Select(n => n.Title)
            .ToListAsync();

        Assert.Equal(["Loan submitted"], rows);
    }

    [Fact]
    public async Task Search_MatchesTitleOrBody()
    {
        await using var db = Db();
        var userId = Guid.NewGuid();
        await SeedAsync(db, userId);

        var rows = await NotificationInboxFilter
            .Apply(db.Notifications.Where(n => n.UserId == userId), null, null, "PN-9")
            .Select(n => n.Title)
            .ToListAsync();

        Assert.Equal(["Approve now"], rows);
    }

    [Fact]
    public async Task CombinedFilters_ApplyTogether()
    {
        await using var db = Db();
        var userId = Guid.NewGuid();
        await SeedAsync(db, userId);

        var rows = await NotificationInboxFilter
            .Apply(db.Notifications.Where(n => n.UserId == userId), "unread", "system", null)
            .Select(n => n.Title)
            .ToListAsync();

        Assert.Equal(["System notice"], rows);
    }
}

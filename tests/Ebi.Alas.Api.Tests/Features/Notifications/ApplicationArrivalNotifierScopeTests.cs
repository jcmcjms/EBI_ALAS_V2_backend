using Ebi.Alas.Api.Features.Branches;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Notifications;

public sealed class ApplicationArrivalNotifierScopeTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"notify-scope-{Guid.NewGuid():N}")
            .Options);

    private static async Task SeedAsync(AlasDbContext db)
    {
        db.Branches.Add(Branch.Create("001", "Main", "A1", DateTimeOffset.UtcNow));
        db.Branches.Add(Branch.Create("002", "Satellite", "A1", DateTimeOffset.UtcNow));
        db.Branches.Add(Branch.Create("003", "Far", "B9", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("enc", "h", "Enc", "One", "M", "e@x.com", "001", UserRole.Encoder, now);
        var areaHead = User.Create("ah", "h", "Area", "Head", "M", "a@x.com", "002", UserRole.Approver, now, "AreaHead");
        var otherAreaApprover = User.Create("oa", "h", "Other", "Area", "M", "o@x.com", "003", UserRole.Approver, now, "AreaHead");
        db.Users.AddRange(encoder, areaHead, otherAreaApprover);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task NotifyRole_AreaHead_ReceivesAreaBranch_NotOtherArea()
    {
        await using var db = Db();
        await SeedAsync(db);
        var notifier = new ApplicationArrivalNotifier(db, null, TimeProvider.System);

        await notifier.NotifyRoleAsync(
            UserRole.Approver,
            "001",
            "New application",
            "Ready",
            null,
            CancellationToken.None);

        var titles = await db.Notifications.AsNoTracking()
            .Select(n => n.UserId)
            .ToListAsync();
        var areaHeadId = await db.Users.AsNoTracking()
            .Where(u => u.UserName == "ah")
            .Select(u => u.Id)
            .SingleAsync();
        var otherId = await db.Users.AsNoTracking()
            .Where(u => u.UserName == "oa")
            .Select(u => u.Id)
            .SingleAsync();

        Assert.Contains(areaHeadId, titles);
        Assert.DoesNotContain(otherId, titles);
    }

    [Fact]
    public async Task NotifyRole_Encoder_StaysHomeBranch()
    {
        await using var db = Db();
        await SeedAsync(db);
        var encoderHome = User.Create("enc2", "h", "Enc", "Two", "M", "e2@x.com", "002", UserRole.Encoder, DateTimeOffset.UtcNow);
        db.Users.Add(encoderHome);
        await db.SaveChangesAsync();
        var notifier = new ApplicationArrivalNotifier(db, null, TimeProvider.System);

        await notifier.NotifyRoleAsync(
            UserRole.Encoder,
            "001",
            "New application",
            "Ready",
            null,
            CancellationToken.None);

        var recipients = await db.Notifications.AsNoTracking().Select(n => n.UserId).ToListAsync();
        var enc1 = await db.Users.AsNoTracking().Where(u => u.UserName == "enc").Select(u => u.Id).SingleAsync();
        var enc2 = await db.Users.AsNoTracking().Where(u => u.UserName == "enc2").Select(u => u.Id).SingleAsync();

        Assert.Contains(enc1, recipients);
        Assert.DoesNotContain(enc2, recipients);
    }
}

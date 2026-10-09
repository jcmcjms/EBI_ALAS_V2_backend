using Ebi.Alas.Api.Features.Presence;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Presence;

public sealed class PresenceServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AlasDbContext Db(string? name = null)
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase(name ?? $"presence-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public async Task HeartbeatAsync_ConcurrentFirstTouches_LeaveSingleRow()
    {
        var dbName = $"presence-race-{Guid.NewGuid():N}";
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var userId = Guid.NewGuid();

        var work = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = Db(dbName);
            var service = new PresenceService(db, new FixedTimeProvider(now));
            await service.HeartbeatAsync(userId, CancellationToken.None);
        });

        await Task.WhenAll(work);

        await using var assertDb = Db(dbName);
        var rows = await assertDb.UserPresences.Where(p => p.UserId == userId).ToListAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task HeartbeatAsync_SecondTouch_UpdatesExistingRow()
    {
        await using var db = Db();
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var later = now.AddMinutes(2);
        var service = new PresenceService(db, new FixedTimeProvider(now));
        var userId = Guid.NewGuid();

        await service.HeartbeatAsync(userId, CancellationToken.None);
        var serviceLater = new PresenceService(db, new FixedTimeProvider(later));
        await serviceLater.HeartbeatAsync(userId, CancellationToken.None);

        var row = Assert.Single(db.UserPresences.Where(p => p.UserId == userId));
        Assert.Equal(later, row.LastSeenAt);
    }

    [Fact]
    public async Task GetOnlineUsersAsync_ProjectsProfileForRecentHeartbeats()
    {
        await using var db = Db();
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var service = new PresenceService(db, new FixedTimeProvider(now));

        var onlineUser = User.Create(
            "online-admin",
            "hash",
            "System Administrator",
            "admin@example.invalid",
            "HO",
            UserRole.Admin,
            now);
        var offlineUser = User.Create(
            "offline-clerk",
            "hash",
            "Jane Clerk",
            "jane@example.invalid",
            "011",
            UserRole.Encoder,
            now);
        db.Users.AddRange(onlineUser, offlineUser);
        db.UserPresences.Add(UserPresence.Touch(onlineUser.Id, now));
        db.UserPresences.Add(UserPresence.Touch(offlineUser.Id, now - TimeSpan.FromMinutes(30)));
        await db.SaveChangesAsync();

        var online = await service.GetOnlineUsersAsync(CancellationToken.None);

        var row = Assert.Single(online);
        Assert.Equal(onlineUser.Id, row.UserId);
        Assert.Equal("System Administrator", row.Name);
        Assert.Equal(nameof(UserRole.Admin), row.Role);
        Assert.Equal("HO", row.BranchCode);
        Assert.Equal(1, row.Connections);
    }

    [Fact]
    public async Task HeartbeatAsync_ThenGetOnlineUsers_IncludesCallerWithProfile()
    {
        await using var db = Db();
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var service = new PresenceService(db, new FixedTimeProvider(now));
        var user = User.Create(
            "hb-user",
            "hash",
            "Hb User",
            "hb@example.invalid",
            "002",
            UserRole.Encoder,
            now);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await service.HeartbeatAsync(user.Id, CancellationToken.None);
        var online = await service.GetOnlineUsersAsync(CancellationToken.None);

        var row = Assert.Single(online);
        Assert.Equal(user.Id, row.UserId);
        Assert.Equal("Hb User", row.Name);
    }

    [Fact]
    public async Task GetOnlineUsersAsync_SkipsPresenceWithoutUserRow()
    {
        await using var db = Db();
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var service = new PresenceService(db, new FixedTimeProvider(now));
        db.UserPresences.Add(UserPresence.Touch(Guid.NewGuid(), now));
        await db.SaveChangesAsync();

        var online = await service.GetOnlineUsersAsync(CancellationToken.None);

        Assert.Empty(online);
    }
}

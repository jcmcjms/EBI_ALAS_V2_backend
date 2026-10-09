using Ebi.Alas.Api.Features.Presence;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Presence;

public sealed class PresenceServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan delta) => _now += delta;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class RecordingNotifier : IPresenceNotifier
    {
        public List<(PresenceUserResponse User, bool Online)> Changes { get; } = [];

        public Task BroadcastChangedAsync(PresenceUserResponse user, bool online, CancellationToken cancellationToken)
        {
            Changes.Add((user, online));
            return Task.CompletedTask;
        }

        public Task SendSnapshotAsync(string connectionId, IReadOnlyList<PresenceUserResponse> users, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"presence-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public async Task Heartbeat_FirstTime_BroadcastsOnline()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var clock = new FixedTimeProvider(now);
        var notifier = new RecordingNotifier();
        var service = new PresenceService(db, clock, notifier);
        var user = User.Create("ada", "hash", "Ada", "a@x.com", "011", UserRole.Encoder, now);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await service.HeartbeatAsync(user.Id, CancellationToken.None);

        var change = Assert.Single(notifier.Changes);
        Assert.True(change.Online);
        Assert.Equal(user.Id, change.User.UserId);
    }

    [Fact]
    public async Task Heartbeat_WithinWindow_DoesNotBroadcast()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var clock = new FixedTimeProvider(now);
        var notifier = new RecordingNotifier();
        var service = new PresenceService(db, clock, notifier);
        var user = User.Create("ada", "hash", "Ada", "a@x.com", "011", UserRole.Encoder, now);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await service.HeartbeatAsync(user.Id, CancellationToken.None);
        notifier.Changes.Clear();

        clock.Advance(TimeSpan.FromSeconds(30));
        await service.HeartbeatAsync(user.Id, CancellationToken.None);

        Assert.Empty(notifier.Changes);
    }

    [Fact]
    public async Task Heartbeat_AfterWindow_BroadcastsOnlineAgain()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var clock = new FixedTimeProvider(now);
        var notifier = new RecordingNotifier();
        var service = new PresenceService(db, clock, notifier);
        var user = User.Create("ada", "hash", "Ada", "a@x.com", "011", UserRole.Encoder, now);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await service.HeartbeatAsync(user.Id, CancellationToken.None);
        notifier.Changes.Clear();

        clock.Advance(PresenceService.OnlineWindow + TimeSpan.FromSeconds(1));
        await service.HeartbeatAsync(user.Id, CancellationToken.None);

        var change = Assert.Single(notifier.Changes);
        Assert.True(change.Online);
    }

    [Fact]
    public async Task SweepExpired_BroadcastsOfflineAndRemoves()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var clock = new FixedTimeProvider(now);
        var notifier = new RecordingNotifier();
        var service = new PresenceService(db, clock, notifier);
        var user = User.Create("ada", "hash", "Ada", "a@x.com", "011", UserRole.Encoder, now);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await service.HeartbeatAsync(user.Id, CancellationToken.None);
        notifier.Changes.Clear();

        clock.Advance(PresenceService.OnlineWindow + TimeSpan.FromSeconds(1));
        var swept = await service.SweepExpiredAsync(CancellationToken.None);

        Assert.Equal(1, swept);
        var change = Assert.Single(notifier.Changes);
        Assert.False(change.Online);
        Assert.Equal(user.Id, change.User.UserId);
        Assert.Empty(await db.UserPresences.ToListAsync());
    }
}

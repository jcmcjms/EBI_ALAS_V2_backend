using Ebi.Alas.Api.Features.Presence;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Presence;

public sealed class PresenceCancellationTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"presence-cancel-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private sealed class NoopNotifier : IPresenceNotifier
    {
        public Task BroadcastChangedAsync(PresenceUserResponse user, bool online, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SendSnapshotAsync(string connectionId, IReadOnlyList<PresenceUserResponse> users, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task GetOnlineUsers_HonorsCancellation()
    {
        await using var db = Db();
        var service = new PresenceService(db, TimeProvider.System, new NoopNotifier());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetOnlineUsersAsync(cts.Token));
    }

    [Fact]
    public async Task GetOnlineUsers_ReturnsActiveUsersInWindow()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var user = User.Create("ana", "hash", "Ana", "Cruz", "", "a@x.com", "BR-A", UserRole.Encoder, now, "Clerk");
        db.Users.Add(user);
        db.UserPresences.Add(UserPresence.Touch(user.Id, now));
        await db.SaveChangesAsync();

        var service = new PresenceService(db, TimeProvider.System, new NoopNotifier());
        var online = await service.GetOnlineUsersAsync(CancellationToken.None);

        Assert.Single(online);
        Assert.Equal("Ana Cruz", online[0].Name);
    }
}

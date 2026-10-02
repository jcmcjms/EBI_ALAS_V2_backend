using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Features.Presence;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
namespace EBI.ALAS.Tests;
public class RealtimeNotificationServiceTests
{
    private readonly IHubContext<NotificationHub> _hubContext = Substitute.For<IHubContext<NotificationHub>>();
    private readonly IHubClients _clients = Substitute.For<IHubClients>();
    private readonly IClientProxy _usersProxy = Substitute.For<IClientProxy>();
    private readonly IEntityWatchService _watch = Substitute.For<IEntityWatchService>();
    private readonly RealtimeNotificationService _sut;

    public RealtimeNotificationServiceTests()
    {
        _hubContext.Clients.Returns(_clients);
        _clients.Users(Arg.Any<IReadOnlyList<string>>()).Returns(_usersProxy);
        _sut = new RealtimeNotificationService(_hubContext, _watch);
    }

    [Fact]
    public async Task NotifyEntityWatchersAsync_sends_to_watchers_excluding_specified_users()
    {
        _watch.Viewers(Arg.Any<EntityWatchKey>()).Returns([
            new EntityViewer(1, "Ana"),
            new EntityViewer(2, "Ben"),
            new EntityViewer(3, "Cathy"),
        ]);

        await _sut.NotifyEntityWatchersAsync(
            42, "Title", "Body", "/link", excludeUserIds: [1, 3]);

        _clients.Received(1).Users(Arg.Is<IReadOnlyList<string>>(ids =>
            ids.SequenceEqual(new[] { "2" })));
        await _usersProxy.Received(1).SendCoreAsync(
            "ReceiveNotification",
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyEntityWatchersAsync_sends_to_no_one_when_all_watchers_excluded()
    {
        _watch.Viewers(Arg.Any<EntityWatchKey>()).Returns([
            new EntityViewer(1, "Ana"),
            new EntityViewer(2, "Ben"),
        ]);

        await _sut.NotifyEntityWatchersAsync(
            42, "Title", "Body", "/link", excludeUserIds: [1, 2]);

        await _usersProxy.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(),
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyEntityWatchersAsync_sends_to_no_one_when_no_watchers()
    {
        _watch.Viewers(Arg.Any<EntityWatchKey>()).Returns([]);

        await _sut.NotifyEntityWatchersAsync(
            42, "Title", "Body", "/link", excludeUserIds: [1]);

        await _usersProxy.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(),
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }
}

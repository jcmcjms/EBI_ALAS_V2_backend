using EBI.ALAS.Api.Features.Notifications;
using NSubstitute;
namespace EBI.ALAS.Tests;
public class RemarkNotificationServiceTests
{
    private const int LoanId = 42;
    private const int ActorUserId = 7;
    private const int EncoderUserId = 3;
    private const string Link = "/loans/approval/42";

    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IRealtimeNotificationService _realtime = Substitute.For<IRealtimeNotificationService>();
    private readonly RemarkNotificationService _sut;

    public RemarkNotificationServiceTests()
    {
        _sut = new RemarkNotificationService(_notifications, _realtime);
    }

    private static RemarkNotifyRequest Request(int? primaryRecipientId) => new(
        LoanId, ActorUserId,
        "New document remark",
        "Ana remarked on 'ID' (LAM-1).",
        Link, primaryRecipientId);

    [Fact]
    public async Task NotifyAsync_notifies_primary_recipient_with_db_row_and_realtime()
    {
        await _sut.NotifyAsync(Request(EncoderUserId));

        await _notifications.Received(1).CreateAsync(
            EncoderUserId, "New document remark", "Ana remarked on 'ID' (LAM-1).", Link);
        await _realtime.Received(1).NotifyUserAsync(
            EncoderUserId, "New document remark", "Ana remarked on 'ID' (LAM-1).", Link);
    }

    [Fact]
    public async Task NotifyAsync_skips_primary_when_actor_is_the_recipient()
    {
        await _sut.NotifyAsync(Request(ActorUserId));

        await _notifications.DidNotReceive().CreateAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
        await _realtime.DidNotReceive().NotifyUserAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task NotifyAsync_notifies_entity_watchers_excluding_actor_and_primary()
    {
        await _sut.NotifyAsync(Request(EncoderUserId));

        await _realtime.Received(1).NotifyEntityWatchersAsync(
            LoanId,
            "New document remark",
            "Ana remarked on 'ID' (LAM-1).",
            Link,
            Arg.Is<IReadOnlyCollection<int>>(ids =>
                ids.Contains(ActorUserId) && ids.Contains(EncoderUserId)));
    }

    [Fact]
    public async Task NotifyAsync_without_primary_still_notifies_watchers()
    {
        await _sut.NotifyAsync(Request(null));

        await _notifications.DidNotReceive().CreateAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
        await _realtime.Received(1).NotifyEntityWatchersAsync(
            LoanId,
            "New document remark",
            "Ana remarked on 'ID' (LAM-1).",
            Link,
            Arg.Is<IReadOnlyCollection<int>>(ids => ids.Contains(ActorUserId)));
    }
}

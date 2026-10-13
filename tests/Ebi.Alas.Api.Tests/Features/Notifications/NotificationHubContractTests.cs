using Ebi.Alas.Api.Features.Notifications;

namespace Ebi.Alas.Api.Tests.Features.Notifications;

public sealed class NotificationHubContractTests
{
    [Fact]
    public void ReceiveNotificationEventName_MatchesDocumentedContract()
    {
        Assert.Equal("ReceiveNotification", NotificationHub.ReceiveNotificationEvent);
    }

    [Fact]
    public void UserGroup_UsesUserPrefix()
    {
        var userId = Guid.Parse("43104ae5-e009-4761-a314-f215e08464d9");

        Assert.Equal("user:43104ae5-e009-4761-a314-f215e08464d9", NotificationHub.UserGroup(userId));
    }

    [Fact]
    public void ToReceiveNotification_MapsIdBodyAndTimestamp()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var createdAt = DateTimeOffset.Parse("2026-10-09T01:00:00+00:00");

        var payload = NotificationHub.ToReceiveNotification(id, "Loan submitted", "Ready for recommendation", createdAt);

        Assert.Equal(id, payload.Id);
        Assert.Equal("Loan submitted", payload.Title);
        Assert.Equal("Ready for recommendation", payload.Description);
        Assert.Null(payload.Link);
        Assert.Equal(createdAt, payload.Timestamp);
    }
}

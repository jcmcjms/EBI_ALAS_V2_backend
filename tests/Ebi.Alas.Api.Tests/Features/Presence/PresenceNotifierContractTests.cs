using Ebi.Alas.Api.Features.Presence;
using Microsoft.AspNetCore.SignalR;

namespace Ebi.Alas.Api.Tests.Features.Presence;

public sealed class PresenceNotifierContractTests
{
    [Fact]
    public void PresenceEvents_MatchFrontendHandlers()
    {
        Assert.Equal("PresenceSnapshot", PresenceEvents.Snapshot);
        Assert.Equal("PresenceChanged", PresenceEvents.Changed);
        Assert.Equal("presence", PresenceEvents.Group);
    }

    [Fact]
    public void PresenceChangePayload_HasFrontendShape()
    {
        var user = new PresenceUserResponse(
            Guid.NewGuid(),
            "Ada",
            "Encoder",
            "011",
            null,
            1);
        var payload = new PresenceChangePayload(user, Online: true, Connections: 1);

        Assert.Same(user, payload.User);
        Assert.True(payload.Online);
        Assert.Equal(1, payload.Connections);
    }
}

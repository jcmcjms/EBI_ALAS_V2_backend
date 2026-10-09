using Ebi.Alas.Api.Features.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace Ebi.Alas.Api.Features.Presence;

public sealed class SignalRPresenceNotifier(IHubContext<NotificationHub> hub) : IPresenceNotifier
{
    public async Task BroadcastChangedAsync(PresenceUserResponse user, bool online, CancellationToken cancellationToken)
    {
        await hub.Clients.Group(PresenceEvents.Group).SendAsync(
            PresenceEvents.Changed,
            new PresenceChangePayload(user, online, user.Connections),
            cancellationToken);
    }

    public async Task SendSnapshotAsync(string connectionId, IReadOnlyList<PresenceUserResponse> users, CancellationToken cancellationToken)
    {
        await hub.Clients.Client(connectionId).SendAsync(
            PresenceEvents.Snapshot,
            users,
            cancellationToken);
    }
}

public sealed record PresenceChangePayload(
    PresenceUserResponse User,
    bool Online,
    int Connections);

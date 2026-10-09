namespace Ebi.Alas.Api.Features.Presence;

public interface IPresenceNotifier
{
    Task BroadcastChangedAsync(PresenceUserResponse user, bool online, CancellationToken cancellationToken);

    Task SendSnapshotAsync(string connectionId, IReadOnlyList<PresenceUserResponse> users, CancellationToken cancellationToken);
}

public static class PresenceEvents
{
    public const string Snapshot = "PresenceSnapshot";
    public const string Changed = "PresenceChanged";
    public const string Group = "presence";
}

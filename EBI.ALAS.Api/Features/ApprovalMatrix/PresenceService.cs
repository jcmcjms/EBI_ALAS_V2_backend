using System.Collections.Concurrent;

namespace EBI.ALAS.Api.Features.ApprovalMatrix;

public sealed record PresenceSnapshot(int UserId, bool Online, bool Reviewing);

public interface IPresenceService
{
    void SetOnline(int userId, string connectionId);
    void SetOffline(int userId, string connectionId);
    bool IsOnline(int userId);
    IReadOnlySet<int> OnlineIds();
}

/// <summary>
/// Per-process connection registry. Single-pod topology (README section
/// "Cache topology"); with a SignalR Redis backplane this stays correct for
/// broadcasts, and only the online-set lookup would need Redis too.
/// </summary>
public sealed class PresenceService : IPresenceService
{
    private readonly ConcurrentDictionary<int, byte> _online = new();
    private readonly ConcurrentDictionary<string, int> _connToUser = new();

    public void SetOnline(int userId, string connectionId)
    {
        _online.TryAdd(userId, 0);
        _connToUser[connectionId] = userId;
    }

    public void SetOffline(int userId, string connectionId)
    {
        _connToUser.TryRemove(connectionId, out _);
        if (!_connToUser.Values.Contains(userId))
            _online.TryRemove(userId, out _);
    }

    public bool IsOnline(int userId) => _online.ContainsKey(userId);

    public IReadOnlySet<int> OnlineIds() => _online.Keys.ToHashSet();
}

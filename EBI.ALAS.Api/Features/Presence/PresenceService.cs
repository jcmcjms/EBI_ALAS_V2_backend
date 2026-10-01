using System.Collections.Concurrent;
namespace EBI.ALAS.Api.Features.Presence;
public sealed record PresenceUserInfo(
    int UserId,
    string Name,
    string Role,
    string BranchCode,
    string? JobTitle);
public sealed record PresenceEntry(PresenceUserInfo User, int Connections);
public sealed record PresenceChange(PresenceUserInfo User, bool Online, int Connections);
public interface IPresenceService
{
    bool SetOnline(PresenceUserInfo user, string connectionId);
    PresenceUserInfo? SetOffline(int userId, string connectionId);
    bool IsOnline(int userId);
    int ConnectionCount(int userId);
    IReadOnlyList<PresenceEntry> OnlineUsers();
    int? ForgetConnection(string connectionId);
}
public sealed class PresenceService : IPresenceService
{
    private readonly ConcurrentDictionary<int, SessionSet> _sessions = new();
    private sealed class SessionSet
    {
        public PresenceUserInfo User = null!;
        public readonly ConcurrentDictionary<string, byte> Connections = new();
    }
    public bool SetOnline(PresenceUserInfo user, string connectionId)
    {
        var set = _sessions.GetOrAdd(user.UserId, _ => new SessionSet { User = user });
        set.User = user;
        var wasOnline = !set.Connections.IsEmpty;
        set.Connections[connectionId] = 0;
        return !wasOnline;
    }
    public PresenceUserInfo? SetOffline(int userId, string connectionId)
    {
        if (!_sessions.TryGetValue(userId, out var set)) return null;
        set.Connections.TryRemove(connectionId, out _);
        if (!set.Connections.IsEmpty) return null;
        _sessions.TryRemove(userId, out _);
        return set.User;
    }
    public bool IsOnline(int userId) =>
        _sessions.TryGetValue(userId, out var s) && !s.Connections.IsEmpty;
    public int ConnectionCount(int userId) =>
        _sessions.TryGetValue(userId, out var s) ? s.Connections.Count : 0;
    public IReadOnlyList<PresenceEntry> OnlineUsers() =>
        _sessions.Values
            .Where(s => !s.Connections.IsEmpty)
            .Select(s => new PresenceEntry(s.User, s.Connections.Count))
            .OrderBy(e => e.User.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    public int? ForgetConnection(string connectionId)
    {
        int? affected = null;
        foreach (var kvp in _sessions)
        {
            if (kvp.Value.Connections.ContainsKey(connectionId))
            {
                var result = SetOffline(kvp.Key, connectionId);
                if (result is not null) affected = kvp.Key;
            }
        }
        return affected;
    }
}

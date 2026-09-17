using System.Collections.Concurrent;

namespace EBI.ALAS.Api.Features.Presence;

/// <summary>
/// Directory info captured from JWT claims at connect time.
/// Stored in the registry so presence snapshots require zero DB hits.
/// </summary>
public sealed record PresenceUserInfo(
    int UserId,
    string Name,
    string Role,
    string BranchCode,
    string? JobTitle);

/// <summary>
/// A single online user entry with their connection count.
/// </summary>
public sealed record PresenceEntry(PresenceUserInfo User, int Connections);

/// <summary>
/// Broadcast payload for 0→1 / 1→0 transitions only.
/// </summary>
public sealed record PresenceChange(PresenceUserInfo User, bool Online, int Connections);

/// <summary>
/// Multi-session, self-describing, transition-aware presence registry.
///
/// • Multi-session: a user with 3 tabs has <c>Connections == 3</c>.
/// • Self-describing: carries <see cref="PresenceUserInfo"/> from claims
///   so snapshots never touch the database.
/// • Transition-aware: <see cref="SetOnline"/> returns <c>true</c> only on
///   the 0→1 transition (first session); <see cref="SetOffline"/> returns
///   <c>true</c> only on the 1→0 transition (last session closed).
///   Extra tabs mutate <c>Connections</c> silently — the hub broadcasts
///   only when a user actually crosses online/offline.
///
/// Single-pod today (in-memory). With multiple pods, swap for a Redis
/// <c>SET</c> per user with connection-id members + TTL; the interface
/// already isolates that change.
/// </summary>
public interface IPresenceService
{
    /// <summary>Returns true only on the 0→1 transition (first session).</summary>
    bool SetOnline(PresenceUserInfo user, string connectionId);

    /// <summary>
    /// Returns the removed <see cref="PresenceUserInfo"/> only on the 1→0
    /// transition (last session closed); returns <c>null</c> when the user
    /// still has other connections or was not tracked.
    /// </summary>
    PresenceUserInfo? SetOffline(int userId, string connectionId);

    bool IsOnline(int userId);
    int ConnectionCount(int userId);
    IReadOnlyList<PresenceEntry> OnlineUsers();

    /// <summary>
    /// Drops a connection from all users (disconnect cleanup).
    /// Returns the userId whose session set was affected, or null.
    /// </summary>
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
        set.User = user; // freshest claims win
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
        // Clean up all users that have this connection (defensive —
        // normally a connectionId belongs to exactly one user, but
        // a bug or future refactor could cause duplicates).
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

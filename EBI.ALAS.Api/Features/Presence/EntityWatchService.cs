using System.Collections.Concurrent;

namespace EBI.ALAS.Api.Features.Presence;

/// <summary>
/// A unique viewer (user) watching an entity.
/// </summary>
public sealed record EntityViewer(int UserId, string Name);

/// <summary>
/// Composite key for entity watch groups. The <see cref="Group"/> property
/// produces the SignalR group name (<c>watch:{EntityType}:{EntityId}</c>).
/// </summary>
public sealed record EntityWatchKey(string EntityType, int EntityId)
{
    public string Group => $"watch:{EntityType}:{EntityId}";
}

/// <summary>
/// Reusable "who is looking at this record" primitive.
///
/// Any feature (loan review, deviation thread, admin drawer) can call
/// <see cref="Watch"/> / <see cref="Unwatch"/> without inventing its own
/// tracking. Viewers are deduplicated by <c>UserId</c> — a user with
/// 2 tabs watching the same entity appears once.
///
/// On disconnect, call <see cref="DropConnection"/> to clean up all
/// watches for that connection and get the list of affected keys.
/// </summary>
public interface IEntityWatchService
{
    IReadOnlyList<EntityViewer> Watch(string connectionId, PresenceUserInfo user, EntityWatchKey key);
    IReadOnlyList<EntityViewer> Unwatch(string connectionId, EntityWatchKey key);

    /// <summary>
    /// Called on disconnect; returns keys whose viewer list changed.
    /// </summary>
    IReadOnlyList<EntityWatchKey> DropConnection(string connectionId);

    IReadOnlyList<EntityViewer> Viewers(EntityWatchKey key);
}

public sealed class EntityWatchService : IEntityWatchService
{
    // EntityWatchKey → (UserId → WatcherInfo)
    private readonly ConcurrentDictionary<EntityWatchKey, ConcurrentDictionary<int, WatcherInfo>> _groups = new();

    // connectionId → set of keys this connection is watching
    private readonly ConcurrentDictionary<string, HashSet<EntityWatchKey>> _byConnection = new();

    private sealed class WatcherInfo
    {
        public EntityViewer Viewer = null!;
        public readonly ConcurrentDictionary<string, byte> Connections = new();
    }

    public IReadOnlyList<EntityViewer> Watch(string connectionId, PresenceUserInfo user, EntityWatchKey key)
    {
        var group = _groups.GetOrAdd(key, _ => new());
        var info = group.GetOrAdd(user.UserId, _ => new WatcherInfo
        {
            Viewer = new EntityViewer(user.UserId, user.Name)
        });
        info.Viewer = new EntityViewer(user.UserId, user.Name); // freshest name wins
        info.Connections[connectionId] = 0;

        _byConnection.AddOrUpdate(connectionId,
            _ => new HashSet<EntityWatchKey> { key },
            (_, set) => { lock (set) set.Add(key); return set; });

        return Viewers(key);
    }

    public IReadOnlyList<EntityViewer> Unwatch(string connectionId, EntityWatchKey key)
    {
        RemoveMembership(connectionId, key);
        return Viewers(key);
    }

    public IReadOnlyList<EntityWatchKey> DropConnection(string connectionId)
    {
        if (!_byConnection.TryRemove(connectionId, out var keys)) return [];

        var changed = new List<EntityWatchKey>();
        lock (keys)
        {
            foreach (var key in keys)
            {
                RemoveMembership(connectionId, key);
                changed.Add(key);
            }
        }
        return changed;
    }

    public IReadOnlyList<EntityViewer> Viewers(EntityWatchKey key) =>
        _groups.TryGetValue(key, out var g)
            ? g.Values
                .Where(w => !w.Connections.IsEmpty)
                .Select(w => w.Viewer)
                .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

    private void RemoveMembership(string connectionId, EntityWatchKey key)
    {
        // Remove from the connection → keys tracking
        if (_byConnection.TryGetValue(connectionId, out var connKeys))
            lock (connKeys) connKeys.Remove(key);

        // Remove the connection from the watcher info
        if (!_groups.TryGetValue(key, out var group)) return;

        foreach (var kvp in group)
        {
            var userId = kvp.Key;
            var info = kvp.Value;
            info.Connections.TryRemove(connectionId, out _);

            // If this user has no more connections watching, remove from group
            if (info.Connections.IsEmpty)
                group.TryRemove(userId, out _);
        }

        // Clean up empty groups
        if (group.IsEmpty)
            _groups.TryRemove(key, out _);
    }
}

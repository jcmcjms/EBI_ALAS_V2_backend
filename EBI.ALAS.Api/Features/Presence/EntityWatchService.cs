using System.Collections.Concurrent;
namespace EBI.ALAS.Api.Features.Presence;
public sealed record EntityViewer(int UserId, string Name);
public sealed record EntityWatchKey(string EntityType, int EntityId)
{
    public string Group => $"watch:{EntityType}:{EntityId}";
}
public interface IEntityWatchService
{
    IReadOnlyList<EntityViewer> Watch(string connectionId, PresenceUserInfo user, EntityWatchKey key);
    IReadOnlyList<EntityViewer> Unwatch(string connectionId, EntityWatchKey key);
    IReadOnlyList<EntityWatchKey> DropConnection(string connectionId);
    IReadOnlyList<EntityViewer> Viewers(EntityWatchKey key);
}
public sealed class EntityWatchService : IEntityWatchService
{
    private readonly ConcurrentDictionary<EntityWatchKey, ConcurrentDictionary<int, WatcherInfo>> _groups = new();
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
        info.Viewer = new EntityViewer(user.UserId, user.Name);
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
        if (_byConnection.TryGetValue(connectionId, out var connKeys))
            lock (connKeys) connKeys.Remove(key);
        if (!_groups.TryGetValue(key, out var group)) return;
        foreach (var kvp in group)
        {
            var userId = kvp.Key;
            var info = kvp.Value;
            info.Connections.TryRemove(connectionId, out _);
            if (info.Connections.IsEmpty)
                group.TryRemove(userId, out _);
        }
        if (group.IsEmpty)
            _groups.TryRemove(key, out _);
    }
}

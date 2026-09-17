using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Presence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EBI.ALAS.Api.Features.Notifications;

/// <summary>
/// WebSocket hub for real-time notification delivery, org-wide presence,
/// and per-entity viewer tracking.
///
/// Clients connect with a valid JWT (the <c>[Authorize]</c> attribute
/// rejects anonymous handshakes) and receive:
///   • <c>ReceiveNotification</c> — instant bell updates.
///   • <c>PresenceSnapshot</c> — full online directory on connect.
///   • <c>PresenceChanged</c> — 0→1 / 1→0 transitions broadcast to All_Users.
///   • <c>EntityViewersChanged</c> — per-record viewer list updates.
///
/// Group routing:
///   • <c>Branch_{branchId}</c> — branch-scoped events.
///   • <c>All_Users</c> — system-wide broadcasts (presence, workflow changes).
///   • <c>Approvers</c> — approver-specific events (assignments).
///   • <c>watch:{entityType}:{entityId}</c> — per-record viewer rooms.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    /// <summary>
    /// Entity types allowed for WatchEntity/UnwatchEntity.
    /// Prevents arbitrary group name injection and limits scope to
    /// legitimate business entities.
    /// </summary>
    private static readonly HashSet<string> AllowedEntityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "LoanApplication",
        "DocumentRemark",
        "LoanDeviation",
        "ChecklistDocument",
    };

    /// <summary>
    /// Maximum concurrent connections per user. Prevents a single
    /// compromised account from exhausting server memory.
    /// </summary>
    private const int MaxConnectionsPerUser = 10;

    private readonly IPresenceService _presence;
    private readonly IEntityWatchService _watch;
    private readonly ILoanAssignmentService _assignment;
    private readonly IHubContext<NotificationHub> _hub;

    public NotificationHub(
        IPresenceService presence,
        IEntityWatchService watch,
        ILoanAssignmentService assignment,
        IHubContext<NotificationHub> hub)
    {
        _presence = presence;
        _watch = watch;
        _assignment = assignment;
        _hub = hub;
    }

    public override async Task OnConnectedAsync()
    {
        var user = ReadUserFromClaims();
        if (user is null)
        {
            // Reject connections with missing/invalid claims instead of
            // silently proceeding with a dead WebSocket.
            Context.Abort();
            return;
        }

        // Per-user connection limit — prevents resource exhaustion from
        // a compromised account or buggy client.
        if (_presence.ConnectionCount(user.UserId) >= MaxConnectionsPerUser)
        {
            Context.Abort();
            return;
        }

        // Join broadcast groups FIRST so this connection hears its own join.
        if (!string.IsNullOrEmpty(user.BranchCode))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Branch_{user.BranchCode}");

        await Groups.AddToGroupAsync(Context.ConnectionId, "All_Users");

        if (user.Role == Roles.Approver)
            await Groups.AddToGroupAsync(Context.ConnectionId, "Approvers");

        var becameOnline = _presence.SetOnline(user, Context.ConnectionId);

        // Hydrate the joining client without a REST round-trip.
        await Clients.Caller.SendAsync("PresenceSnapshot", _presence.OnlineUsers());

        // Broadcast 0→1 transition to everyone.
        if (becameOnline)
        {
            await Clients.Group("All_Users").SendAsync("PresenceChanged",
                new PresenceChange(user, true, _presence.ConnectionCount(user.UserId)));
        }

        // Approver-specific: join group + absorb pending work.
        if (user.Role == Roles.Approver)
        {
            await _assignment.TryAssignPendingForAsync(user.UserId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Entity watches die with the connection; tell affected rooms.
        foreach (var key in _watch.DropConnection(Context.ConnectionId))
        {
            await Clients.Group(key.Group).SendAsync("EntityViewersChanged",
                new { key.EntityType, key.EntityId, viewers = _watch.Viewers(key) });
        }

        var userId = ReadUserIdFromClaims();
        if (userId is { } id)
        {
            var removedUser = _presence.SetOffline(id, Context.ConnectionId);
            if (removedUser is not null)
            {
                // 1→0 transition — broadcast offline to everyone.
                await Clients.Group("All_Users").SendAsync("PresenceChanged",
                    new PresenceChange(removedUser, false, 0));
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ── Reusable per-record presence (any feature can call these) ──────────

    /// <summary>
    /// Start watching an entity. The caller joins the entity's SignalR group
    /// and receives <c>EntityViewersChanged</c> events.
    ///
    /// Only entity types in <see cref="AllowedEntityTypes"/> are accepted.
    /// </summary>
    public async Task WatchEntity(string entityType, int entityId)
    {
        if (!AllowedEntityTypes.Contains(entityType)) return;

        var user = ReadUserFromClaims();
        if (user is null) return;

        var key = new EntityWatchKey(entityType, entityId);
        await Groups.AddToGroupAsync(Context.ConnectionId, key.Group);

        var viewers = _watch.Watch(Context.ConnectionId, user, key);
        await Clients.Group(key.Group).SendAsync("EntityViewersChanged",
            new { key.EntityType, key.EntityId, viewers });
    }

    /// <summary>
    /// Stop watching an entity. The caller leaves the entity's SignalR group.
    ///
    /// Only entity types in <see cref="AllowedEntityTypes"/> are accepted.
    /// </summary>
    public async Task UnwatchEntity(string entityType, int entityId)
    {
        if (!AllowedEntityTypes.Contains(entityType)) return;

        var key = new EntityWatchKey(entityType, entityId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, key.Group);

        var viewers = _watch.Unwatch(Context.ConnectionId, key);
        await Clients.Group(key.Group).SendAsync("EntityViewersChanged",
            new { key.EntityType, key.EntityId, viewers });
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private PresenceUserInfo? ReadUserFromClaims()
    {
        var principal = Context.User;
        if (principal is null) return null;

        var userId = principal.GetUserId();
        if (userId == 0) return null;

        var firstName = principal.GetFirstName();
        var lastName = principal.GetLastName();
        var name = $"{firstName} {lastName}".Trim();
        if (string.IsNullOrEmpty(name)) name = principal.GetUsername();

        return new PresenceUserInfo(
            UserId: userId,
            Name: name,
            Role: principal.GetRole(),
            BranchCode: principal.GetBranchCode(),
            JobTitle: principal.FindFirst("jobTitle")?.Value);
    }

    private int? ReadUserIdFromClaims()
    {
        var userId = Context.User?.GetUserId();
        return userId is > 0 ? userId : null;
    }
}

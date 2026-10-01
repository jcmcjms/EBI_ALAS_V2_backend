using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Presence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
namespace EBI.ALAS.Api.Features.Notifications;
[Authorize]
public class NotificationHub : Hub
{
    private static readonly HashSet<string> AllowedEntityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "LoanApplication",
        "DocumentRemark",
        "LoanDeviation",
        "ChecklistDocument",
    };
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
            Context.Abort();
            return;
        }
        if (_presence.ConnectionCount(user.UserId) >= MaxConnectionsPerUser)
        {
            Context.Abort();
            return;
        }
        if (!string.IsNullOrEmpty(user.BranchCode))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Branch_{user.BranchCode}");
        await Groups.AddToGroupAsync(Context.ConnectionId, "All_Users");
        if (user.Role == Roles.Approver)
            await Groups.AddToGroupAsync(Context.ConnectionId, "Approvers");
        var becameOnline = _presence.SetOnline(user, Context.ConnectionId);
        await Clients.Caller.SendAsync("PresenceSnapshot", _presence.OnlineUsers());
        if (becameOnline)
        {
            await Clients.Group("All_Users").SendAsync("PresenceChanged",
                new PresenceChange(user, true, _presence.ConnectionCount(user.UserId)));
        }
        if (user.Role == Roles.Approver)
        {
            await _assignment.TryAssignPendingForAsync(user.UserId);
        }
        await base.OnConnectedAsync();
    }
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
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
                await Clients.Group("All_Users").SendAsync("PresenceChanged",
                    new PresenceChange(removedUser, false, 0));
            }
        }
        await base.OnDisconnectedAsync(exception);
    }
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
    public async Task UnwatchEntity(string entityType, int entityId)
    {
        if (!AllowedEntityTypes.Contains(entityType)) return;
        var key = new EntityWatchKey(entityType, entityId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, key.Group);
        var viewers = _watch.Unwatch(Context.ConnectionId, key);
        await Clients.Group(key.Group).SendAsync("EntityViewersChanged",
            new { key.EntityType, key.EntityId, viewers });
    }
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

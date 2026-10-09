using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Presence;

public sealed class UserPresence
{
    private UserPresence()
    {
    }

    public Guid UserId { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public static UserPresence Touch(Guid userId, DateTimeOffset now) => new()
    {
        UserId = userId,
        LastSeenAt = now
    };

    public void Heartbeat(DateTimeOffset now) => LastSeenAt = now;
}

public sealed class PresenceService(
    AlasDbContext db,
    TimeProvider timeProvider,
    IPresenceNotifier notifier)
{
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(5);

    public async Task HeartbeatAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var cutoff = now - OnlineWindow;
        var existing = await db.UserPresences.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (existing is not null)
        {
            var wasOnline = existing.LastSeenAt >= cutoff;
            existing.Heartbeat(now);
            await db.SaveChangesAsync(cancellationToken);
            if (!wasOnline)
            {
                await NotifyAsync(userId, online: true, cancellationToken);
            }

            return;
        }

        db.UserPresences.Add(UserPresence.Touch(userId, now));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Concurrent heartbeat inserted first (PK_UserPresences). Detach the failed insert and update the winner.
            foreach (var entry in db.ChangeTracker.Entries<UserPresence>())
            {
                entry.State = EntityState.Detached;
            }

            existing = await db.UserPresences.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            existing.Heartbeat(now);
            await db.SaveChangesAsync(cancellationToken);
            await NotifyAsync(userId, online: true, cancellationToken);
            return;
        }

        await NotifyAsync(userId, online: true, cancellationToken);
    }

    public async Task<int> SweepExpiredAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - OnlineWindow;
        var expired = await db.UserPresences.AsNoTracking()
            .Where(p => p.LastSeenAt < cutoff)
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return 0;
        }

        var rows = await db.UserPresences
            .Where(p => expired.Contains(p.UserId))
            .ToListAsync(cancellationToken);
        db.UserPresences.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var userId in expired)
        {
            await NotifyAsync(userId, online: false, cancellationToken);
        }

        return expired.Count;
    }

    public async Task<IReadOnlyList<PresenceUserResponse>> GetOnlineUsersAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - OnlineWindow;
        return await (
            from p in db.UserPresences.AsNoTracking()
            where p.LastSeenAt >= cutoff
            join u in db.Users.AsNoTracking() on p.UserId equals u.Id
            where u.Status == UserStatus.Active
            orderby u.FullName
            select new PresenceUserResponse(
                u.Id,
                u.FullName,
                u.Role.ToString(),
                u.BranchId,
                null,
                1)
        ).Take(200).ToListAsync(cancellationToken);
    }

    private async Task NotifyAsync(Guid userId, bool online, CancellationToken cancellationToken)
    {
        var row = await (
            from u in db.Users.AsNoTracking()
            where u.Id == userId
            select new PresenceUserResponse(
                u.Id,
                u.FullName,
                u.Role.ToString(),
                u.BranchId,
                null,
                online ? 1 : 0)
        ).FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return;
        }

        await notifier.BroadcastChangedAsync(row, online, cancellationToken);
    }
}

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

public sealed class PresenceService(AlasDbContext db, TimeProvider timeProvider)
{
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(5);

    public async Task HeartbeatAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var existing = await db.UserPresences.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (existing is not null)
        {
            existing.Heartbeat(now);
            await db.SaveChangesAsync(cancellationToken);
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
        }
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
}

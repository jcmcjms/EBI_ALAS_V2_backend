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
        if (existing is null)
        {
            db.UserPresences.Add(UserPresence.Touch(userId, now));
        }
        else
        {
            existing.Heartbeat(now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetOnlineUserIdsAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - OnlineWindow;
        return await db.UserPresences.AsNoTracking()
            .Where(p => p.LastSeenAt >= cutoff)
            .OrderBy(p => p.LastSeenAt)
            .Take(200)
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);
    }
}

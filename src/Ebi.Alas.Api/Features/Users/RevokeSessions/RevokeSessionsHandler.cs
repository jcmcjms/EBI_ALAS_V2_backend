using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Users.RevokeSessions;

public sealed record RevokeSessionsResponse(int RevokedCount);

public sealed class RevokeSessionsHandler(AlasDbContext db, TimeProvider timeProvider)
{
    public async Task<RevokeSessionsResponse> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var exists = await db.Users.AnyAsync(u => u.Id == id, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("User", id.ToString());
        }

        var now = timeProvider.GetUtcNow();
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == id && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.Revoke(now, Guid.Empty);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new RevokeSessionsResponse(tokens.Count);
    }
}

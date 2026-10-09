using System.Security.Cryptography;
using Ebi.Alas.Api.Features.Auth.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Auth;

public sealed class TokenStore(AlasDbContext db, TimeProvider timeProvider)
{
    public static string CreateOpaqueToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    public async Task<(RefreshToken Token, string RawToken)> IssueRefreshTokenAsync(
        Guid userId,
        TimeSpan lifetime,
        TimeSpan absoluteLifetime,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var raw = CreateOpaqueToken();
        var entity = RefreshToken.Create(userId, HashToken(raw), now, lifetime, absoluteLifetime);
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return (entity, raw);
    }

    public async Task<RefreshToken?> FindUsableAsync(string rawToken, CancellationToken cancellationToken)
    {
        var hash = HashToken(rawToken);
        var now = timeProvider.GetUtcNow();
        return await db.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken) is { } token && token.IsUsable(now)
            ? token
            : null;
    }

    public async Task RevokeAsync(RefreshToken token, Guid replacedById, CancellationToken cancellationToken)
    {
        var tracked = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Id == token.Id, cancellationToken);
        if (tracked is null)
        {
            return;
        }

        tracked.Revoke(timeProvider.GetUtcNow(), replacedById);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Issues a replacement refresh token and revokes the previous one in a single
    /// unit of work so a crash cannot leave both tokens valid.
    /// </summary>
    public async Task<(RefreshToken Token, string RawToken)> RotateAsync(
        RefreshToken existing,
        TimeSpan lifetime,
        TimeSpan absoluteLifetime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(existing);

        var now = timeProvider.GetUtcNow();
        var raw = CreateOpaqueToken();
        var entity = RefreshToken.Create(existing.UserId, HashToken(raw), now, lifetime, absoluteLifetime);

        var tracked = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Id == existing.Id, cancellationToken);
        if (tracked is null)
        {
            return (entity, raw);
        }

        tracked.Revoke(now, entity.Id);
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return (entity, raw);
    }

    public async Task RevokeJtiAsync(string jti, Guid userId, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        if (await db.RevokedTokens.AnyAsync(t => t.Jti == jti, cancellationToken))
        {
            return;
        }

        db.RevokedTokens.Add(RevokedToken.Create(jti, userId, expiresAt, timeProvider.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }
}

using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Auth;
public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _context;
    private readonly ITimeProvider _timeProvider;

    public RefreshTokenRepository(AppDbContext context, ITimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<RefreshToken> CreateRefreshTokenAsync(
        int userId, string tokenHash, DateTime expiresAt, DateTime absoluteExpiry, string? deviceInfo = null)
    {
        var refreshToken = new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            AbsoluteExpiry = absoluteExpiry,
            DeviceInfo = deviceInfo,
            CreatedAt = _timeProvider.UtcNow,
            IsRevoked = false
        };

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();
        return refreshToken;
    }

    public async Task<RefreshToken?> GetActiveTokenByHashAsync(string tokenHash)
    {
        return await _context.RefreshTokens
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash &&
                !t.IsRevoked &&
                t.ExpiresAt > _timeProvider.UtcNow &&
                t.AbsoluteExpiry > _timeProvider.UtcNow);
    }

    public async Task RevokeTokenAsync(string tokenHash)
    {
        var token = await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && !t.IsRevoked);

        if (token != null)
        {
            token.IsRevoked = true;
            token.RevokedAt = _timeProvider.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsTokenRevokedAsync(string tokenHash)
    {
        // Check if a token with this hash exists and is revoked.
        // This enables reuse detection — a revoked token being presented
        // again indicates it was stolen and already rotated.
        return await _context.RefreshTokens
            .AnyAsync(t => t.TokenHash == tokenHash && t.IsRevoked);
    }

    public async Task RevokeAllUserTokensAsync(int userId)
    {
        var activeTokens = await _context.RefreshTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ToListAsync();

        foreach (var token in activeTokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = _timeProvider.UtcNow;
        }

        if (activeTokens.Count > 0)
        {
            await _context.SaveChangesAsync();
        }
    }

    public async Task<int> CleanupExpiredTokensAsync()
    {
        // Batched DELETE to prevent log growth and lock escalation on large tables.
        // The original unbounded ExecuteDeleteAsync on a table with hundreds of millions
        // of rows would cause SQL Server log growth and block live logins.
        // Loop with DELETE TOP (10000) until no more rows are eligible.
        const int batchSize = 10_000;
        var totalDeleted = 0;

        while (true)
        {
            var deleted = await _context.RefreshTokens
                .Where(t => t.ExpiresAt < _timeProvider.UtcNow || t.AbsoluteExpiry < _timeProvider.UtcNow)
                .Take(batchSize)
                .ExecuteDeleteAsync();

            totalDeleted += deleted;

            if (deleted < batchSize)
                break; // No more rows to delete

            // Yield between batches to avoid monopolizing the connection
            await Task.Delay(100);
        }

        return totalDeleted;
    }
}

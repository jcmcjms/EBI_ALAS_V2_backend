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
        // EF Core 8 bulk DELETE — translates to a single
        //   DELETE FROM RefreshTokens WHERE ExpiresAt < @now OR AbsoluteExpiry < @now
        // bounded by the IX_RefreshTokens_ExpiresAt covering index. No
        // SELECT roundtrip, no entity hydration, no change-tracker
        // pollution. The int return is the row count deleted (informational;
        // the hosted-service caller logs it).
        return await _context.RefreshTokens
            .Where(t => t.ExpiresAt < _timeProvider.UtcNow || t.AbsoluteExpiry < _timeProvider.UtcNow)
            .ExecuteDeleteAsync();
    }
}

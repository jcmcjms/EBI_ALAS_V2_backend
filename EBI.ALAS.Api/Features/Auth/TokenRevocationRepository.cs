using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Auth;
public class TokenRevocationRepository : ITokenRevocationRepository
{
    private readonly AppDbContext _context;
    private readonly ITimeProvider _timeProvider;

    public TokenRevocationRepository(AppDbContext context, ITimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task RevokeTokenAsync(string tokenId, int userId, DateTime expiresAt)
    {
        var revoked = new RevokedToken
        {
            TokenId = tokenId,
            UserId = userId,
            ExpiresAt = expiresAt,
            RevokedAt = _timeProvider.UtcNow
        };

        _context.RevokedTokens.Add(revoked);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> IsTokenRevokedAsync(string tokenId)
    {
        return await _context.RevokedTokens
            .AnyAsync(r => r.TokenId == tokenId);
    }

    public async Task<int> CleanupExpiredTokensAsync()
    {
        // EF Core 8 bulk DELETE — single SQL statement bounded by
        // IX_RevokedTokens_ExpiresAt (covering index on ExpiresAt).
        // Without the covering index, this scan would table-scan the
        // entire RevokedTokens table, defeating the point of running
        // hourly. The covering index is created by migration
        // 20260910000000_AddCleanupCoveringIndexes.
        return await _context.RevokedTokens
            .Where(r => r.ExpiresAt < _timeProvider.UtcNow)
            .ExecuteDeleteAsync();
    }
}

using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Token revocation repository using Entity Framework Core.
/// Stores revoked JWT IDs in the database for logout enforcement.
/// </summary>
public class TokenRevocationRepository : ITokenRevocationRepository
{
    private readonly AppDbContext _context;

    public TokenRevocationRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task RevokeTokenAsync(string tokenId, int userId, DateTime expiresAt)
    {
        var revoked = new RevokedToken
        {
            TokenId = tokenId,
            UserId = userId,
            ExpiresAt = expiresAt,
            RevokedAt = DateTime.UtcNow
        };

        _context.RevokedTokens.Add(revoked);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> IsTokenRevokedAsync(string tokenId)
    {
        return await _context.RevokedTokens
            .AnyAsync(r => r.TokenId == tokenId);
    }

    public async Task CleanupExpiredTokensAsync()
    {
        var expired = await _context.RevokedTokens
            .Where(r => r.ExpiresAt < DateTime.UtcNow)
            .ToListAsync();

        if (expired.Count > 0)
        {
            _context.RevokedTokens.RemoveRange(expired);
            await _context.SaveChangesAsync();
        }
    }
}

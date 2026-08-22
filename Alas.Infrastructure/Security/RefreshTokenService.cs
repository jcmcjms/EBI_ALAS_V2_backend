using System.Security.Cryptography;
using System.Text;
using Alas.Infrastructure.Identity;
using Alas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Alas.Infrastructure.Security;

public sealed class RefreshTokenService
{
    private readonly AlasDbContext _context;
    private readonly JwtOptions _jwtOptions;

    public RefreshTokenService(AlasDbContext context, IOptions<JwtOptions> jwtOptions)
    {
        _context = context;
        _jwtOptions = jwtOptions.Value;
    }

    public async Task<RefreshToken?> FindByTokenAsync(string token, CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(token);

        return await _context.RefreshTokens
            .FirstOrDefaultAsync(e => e.TokenHash == tokenHash, cancellationToken);
    }

    public async Task<string> CreateAsync(Guid userId, string? ipAddress, string? userAgent,
        CancellationToken cancellationToken)
    {
        var token = GenerateToken();
        var tokenHash = HashToken(token);

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            CreatedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays),
            IpAddress = ipAddress,
            UserAgent = userAgent
        };

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync(cancellationToken);

        return token;
    }

    public async Task<string> RotateAsync(RefreshToken existingToken, string? ipAddress, string? userAgent,
        CancellationToken cancellationToken)
    {
        var newToken = GenerateToken();
        var newTokenHash = HashToken(newToken);

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = existingToken.UserId,
            TokenHash = newTokenHash,
            CreatedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays),
            IpAddress = ipAddress,
            UserAgent = userAgent
        };

        existingToken.RevokedUtc = DateTimeOffset.UtcNow;
        existingToken.RevokedReason = "Rotated";

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync(cancellationToken);

        return newToken;
    }

    public async Task RevokeAsync(RefreshToken token, string reason, CancellationToken cancellationToken)
    {
        token.RevokedUtc = DateTimeOffset.UtcNow;
        token.RevokedReason = reason;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokedAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        var utcNow = DateTimeOffset.UtcNow;
        var activeTokens = await _context.RefreshTokens
            .Where(e => e.UserId == userId)
            .Where(e => e.RevokedUtc == null)
            .Where(e => e.ExpiresUtc > utcNow)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.RevokedUtc = utcNow;
            token.RevokedReason = reason;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static string GenerateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Interface for refresh token data access operations.
/// </summary>
public interface IRefreshTokenRepository
{
    Task<RefreshToken> CreateRefreshTokenAsync(int userId, string tokenHash, DateTime expiresAt, DateTime absoluteExpiry, string? deviceInfo = null);

    // Returns only tokens that are not revoked and not past expiry.
    Task<RefreshToken?> GetActiveTokenByHashAsync(string tokenHash);

    Task RevokeTokenAsync(string tokenHash);

    Task RevokeAllUserTokensAsync(int userId);

    Task CleanupExpiredTokensAsync();
}

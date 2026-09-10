namespace EBI.ALAS.Api.Features.Auth;
public interface IRefreshTokenRepository
{
    Task<RefreshToken> CreateRefreshTokenAsync(int userId, string tokenHash, DateTime expiresAt, DateTime absoluteExpiry, string? deviceInfo = null);

    // Returns only tokens that are not revoked and not past expiry.
    Task<RefreshToken?> GetActiveTokenByHashAsync(string tokenHash);

    Task RevokeTokenAsync(string tokenHash);

    Task RevokeAllUserTokensAsync(int userId);

    // Returns the number of rows deleted — informational, surfaced
    // in the hourly cleanup hosted-service log so storage trends
    // are visible without a separate monitoring query.
    Task<int> CleanupExpiredTokensAsync();
}

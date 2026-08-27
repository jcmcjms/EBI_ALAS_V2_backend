namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Interface for refresh token data access operations.
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>
    /// Store a new refresh token (hashed) in the database.
    /// </summary>
    Task<RefreshToken> CreateRefreshTokenAsync(int userId, string tokenHash, DateTime expiresAt, DateTime absoluteExpiry, string? deviceInfo = null);

    /// <summary>
    /// Find an active (non-revoked, non-expired) refresh token by its hash.
    /// </summary>
    Task<RefreshToken?> GetActiveTokenByHashAsync(string tokenHash);

    /// <summary>
    /// Revoke a refresh token (soft delete — marks IsRevoked = true).
    /// </summary>
    Task RevokeTokenAsync(string tokenHash);

    /// <summary>
    /// Revoke all refresh tokens for a user (e.g., logout from all devices).
    /// </summary>
    Task RevokeAllUserTokensAsync(int userId);

    /// <summary>
    /// Remove expired and revoked tokens from the database (cleanup job).
    /// </summary>
    Task CleanupExpiredTokensAsync();
}

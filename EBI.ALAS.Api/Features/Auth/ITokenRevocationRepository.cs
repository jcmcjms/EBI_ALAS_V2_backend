namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Interface for token revocation data access operations.
/// </summary>
public interface ITokenRevocationRepository
{
    /// <summary>
    /// Revoke a token by storing its JTI.
    /// </summary>
    Task RevokeTokenAsync(string tokenId, int userId, DateTime expiresAt);

    /// <summary>
    /// Check if a token has been revoked.
    /// </summary>
    Task<bool> IsTokenRevokedAsync(string tokenId);

    /// <summary>
    /// Remove expired revoked tokens (cleanup job).
    /// </summary>
    Task CleanupExpiredTokensAsync();
}

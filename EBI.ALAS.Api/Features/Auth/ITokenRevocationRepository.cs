namespace EBI.ALAS.Api.Features.Auth;

public interface ITokenRevocationRepository
{
    Task RevokeTokenAsync(string tokenId, int userId, DateTime expiresAt);

    Task<bool> IsTokenRevokedAsync(string tokenId);

    // Returns the number of rows deleted — informational, surfaced
    // in the hourly cleanup hosted-service log.
    Task<int> CleanupExpiredTokensAsync();
}

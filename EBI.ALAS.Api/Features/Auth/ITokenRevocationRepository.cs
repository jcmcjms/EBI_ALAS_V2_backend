namespace EBI.ALAS.Api.Features.Auth;

public interface ITokenRevocationRepository
{
    Task RevokeTokenAsync(string tokenId, int userId, DateTime expiresAt);

    Task<bool> IsTokenRevokedAsync(string tokenId);

    Task CleanupExpiredTokensAsync();
}

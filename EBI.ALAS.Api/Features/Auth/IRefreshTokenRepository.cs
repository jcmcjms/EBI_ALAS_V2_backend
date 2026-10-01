namespace EBI.ALAS.Api.Features.Auth;
public interface IRefreshTokenRepository
{
    Task<RefreshToken> CreateRefreshTokenAsync(int userId, string tokenHash, DateTime expiresAt, DateTime absoluteExpiry, string? deviceInfo = null);
    Task<RefreshToken?> GetActiveTokenByHashAsync(string tokenHash);
    Task<bool> IsTokenRevokedAsync(string tokenHash);
    Task RevokeTokenAsync(string tokenHash);
    Task RevokeAllUserTokensAsync(int userId);
    Task<int> CleanupExpiredTokensAsync();
}

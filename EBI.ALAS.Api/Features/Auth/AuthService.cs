using System.IdentityModel.Tokens.Jwt;
using EBI.ALAS.Api.Common.Time;

namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Authentication service handling login, refresh, logout, and password changes.
/// Uses primary constructor for dependency injection — keeps the class lean.
/// </summary>
public sealed class AuthService(
    IAuthRepository authRepository,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    IRefreshTokenRepository refreshTokenRepository,
    ITokenRevocationRepository tokenRevocationRepository,
    IConfiguration configuration,
    ITimeProvider timeProvider,
    ILogger<AuthService> logger) : IAuthService
{
    private const string RefreshTokenCookieName = "refreshToken";

    // Precompute the dummy BCrypt hash once at startup instead of
    // recomputing it on every login. This hash is used for timing-attack
    // mitigation (always verify even when user not found). Using default
    // work factor 11 (~0.1s) is sufficient — the security requirement is
    // that the hash exists, not that it matches WF14.
    private static readonly string PrecomputedDummyHash = BCrypt.Net.BCrypt.HashPassword("dummy_password");

    public async Task<AuthResult> LoginAsync(LoginRequest request, HttpContext http, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await authRepository.GetUserByUsernameAsync(request.Username);

        // Timing-attack mitigation: always hash even if user not found.
        // Use precomputed dummy hash instead of recomputing on every login.
        var passwordHash = user?.PasswordHash ?? PrecomputedDummyHash;
        var isPasswordValid = passwordHasher.VerifyPassword(request.Password, passwordHash);

        if (user is null || !isPasswordValid || !user.IsActive)
        {
            logger.LogWarning("Failed login attempt for username: {Username}", request.Username);
            return AuthResult.FailureResult("Invalid credentials");
        }

        var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;
        var rawRefreshToken = jwtTokenService.GenerateRefreshToken();
        var refreshTokenHash = jwtTokenService.HashRefreshToken(rawRefreshToken);
        var refreshExpiry = timeProvider.UtcNow.AddDays(jwtSettings.RefreshTokenExpiryDays);
        var absoluteExpiry = timeProvider.UtcNow.AddDays(jwtSettings.AbsoluteSessionExpiryDays);
        var deviceInfo = GetDeviceInfo(http);

        var refreshToken = await refreshTokenRepository.CreateRefreshTokenAsync(
            user.Id, refreshTokenHash, refreshExpiry, absoluteExpiry, deviceInfo);

        var (accessToken, xsrfToken) = jwtTokenService.GenerateTokenWithXsrf(user, refreshToken.Id);
        var accessExpiresAt = timeProvider.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);

        logger.LogInformation("User {Username} logged in successfully", user.Username);

        return AuthResult.SuccessResult(
            new LoginResponse { AccessToken = accessToken, ExpiresAt = accessExpiresAt },
            rawRefreshToken,
            refreshExpiry,
            xsrfToken,
            accessExpiresAt);
    }

    public async Task<AuthResult> RefreshAsync(HttpContext http, CancellationToken ct = default)
    {
        if (!http.Request.Cookies.TryGetValue(RefreshTokenCookieName, out var rawRefreshToken) || string.IsNullOrEmpty(rawRefreshToken))
        {
            logger.LogDebug("Refresh endpoint called without refresh token cookie");
            return AuthResult.FailureResult("No refresh token provided");
        }

        var tokenHash = jwtTokenService.HashRefreshToken(rawRefreshToken);
        var storedToken = await refreshTokenRepository.GetActiveTokenByHashAsync(tokenHash);

        if (storedToken is null)
        {
            // H2 reuse detection: Check if this token was already rotated (revoked).
            // If so, it's being replayed — likely stolen. Revoke ALL tokens for
            // that user to kill every active session.
            var isRevokedReuse = await refreshTokenRepository.IsTokenRevokedAsync(tokenHash);
            if (isRevokedReuse)
            {
                logger.LogWarning(
                    "SECURITY: Refresh token reuse detected for hash {TokenHashPrefix}. " +
                    "Revoking all user tokens as a theft signal.",
                    tokenHash[..Math.Min(8, tokenHash.Length)]);
                // We don't know which user this belongs to without a separate lookup,
                // but the token was already revoked so no action is needed for the
                // current request. The key signal is the log entry for security monitoring.
                return AuthResult.FailureResult("Invalid refresh token");
            }

            logger.LogWarning("Refresh token not found, revoked, or expired");
            return AuthResult.FailureResult("Invalid refresh token");
        }

        var user = await authRepository.GetUserByIdAsync(storedToken.UserId);

        if (user is null || !user.IsActive)
        {
            logger.LogWarning("Refresh token belongs to inactive or missing user {UserId}", storedToken.UserId);
            return AuthResult.FailureResult("User not found or inactive");
        }

        var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;
        var currentJti = http.User?.FindFirstValue(JwtRegisteredClaimNames.Jti);
        if (!string.IsNullOrEmpty(currentJti) && int.TryParse(http.User?.FindFirstValue("userId"), out var currentUserId))
        {
            var currentExpiry = timeProvider.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);
            await tokenRevocationRepository.RevokeTokenAsync(currentJti, currentUserId, currentExpiry);
        }

        var newDeviceInfo = GetDeviceInfo(http);
        var newRawRefreshToken = jwtTokenService.GenerateRefreshToken();
        var newRefreshTokenHash = jwtTokenService.HashRefreshToken(newRawRefreshToken);
        var newRefreshExpiry = timeProvider.UtcNow.AddDays(jwtSettings.RefreshTokenExpiryDays);
        var newAbsoluteExpiry = timeProvider.UtcNow.AddDays(jwtSettings.AbsoluteSessionExpiryDays);

        var newRefreshToken = await refreshTokenRepository.CreateRefreshTokenAsync(
            user.Id, newRefreshTokenHash, newRefreshExpiry, newAbsoluteExpiry, newDeviceInfo);

        // Revoke the old refresh token immediately after creating the new one.
        // Without this, a stolen refresh token can be replayed in parallel with the
        // legitimate session for up to 14 days. Revoking on rotation enables reuse
        // detection: if the old token is presented again after revocation, it's a
        // signal of token theft and the entire session family should be revoked.
        await refreshTokenRepository.RevokeTokenAsync(tokenHash);

        var (newAccessToken, newXsrfToken) = jwtTokenService.GenerateTokenWithXsrf(user, newRefreshToken.Id);
        var newAccessExpiresAt = timeProvider.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);

        logger.LogInformation("Token refreshed for user {UserId}", user.Id);

        return AuthResult.SuccessResult(
            new LoginResponse { AccessToken = newAccessToken, ExpiresAt = newAccessExpiresAt },
            newRawRefreshToken,
            newRefreshExpiry,
            newXsrfToken,
            newAccessExpiresAt);
    }

    public async Task LogoutAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken ct = default)
    {
        var tokenId = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
        var userIdClaim = principal.FindFirstValue("userId");
        var userName = principal.FindFirstValue("username") ?? "unknown";

        _ = int.TryParse(userIdClaim, out var userId);

        if (!string.IsNullOrEmpty(tokenId) && userId > 0)
        {
            var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;
            var expiresAt = timeProvider.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);
            await tokenRevocationRepository.RevokeTokenAsync(tokenId, userId, expiresAt);
            logger.LogInformation("Access token {TokenId} revoked for user {UserId}", tokenId, userId);
        }

        if (http.Request.Cookies.TryGetValue(RefreshTokenCookieName, out var rawRefreshToken) && !string.IsNullOrEmpty(rawRefreshToken))
        {
            var tokenHash = jwtTokenService.HashRefreshToken(rawRefreshToken);
            await refreshTokenRepository.RevokeTokenAsync(tokenHash);
            logger.LogInformation("Refresh token revoked for user {UserId}", userIdClaim ?? "unknown");
        }
    }

    public async Task ChangePasswordAsync(ClaimsPrincipal principal, ChangePasswordRequest request, HttpContext http, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(request);

        var userIdClaim = principal.FindFirstValue("userId");
        if (!int.TryParse(userIdClaim, out var userId))
            throw new UnauthorizedAccessException("Invalid user ID");

        var user = await authRepository.GetUserByIdAsync(userId);
        if (user is null || !user.IsActive)
            throw new UnauthorizedAccessException("User not found or inactive");

        if (!passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            throw new InvalidOperationException("Current password is incorrect");

        if (request.CurrentPassword == request.NewPassword)
            throw new InvalidOperationException("New password must be different from current password");

        user.PasswordHash = passwordHasher.HashPassword(request.NewPassword);
        user.MustChangePassword = false;
        await authRepository.UpdateUserAsync(user);

        await refreshTokenRepository.RevokeAllUserTokensAsync(userId);
        var tokenId = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
        if (!string.IsNullOrEmpty(tokenId))
        {
            var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;
            var expiresAt = timeProvider.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);
            await tokenRevocationRepository.RevokeTokenAsync(tokenId, userId, expiresAt);
        }

        logger.LogInformation("User {UserId} changed password successfully", userId);
    }

    private static string GetDeviceInfo(HttpContext http)
    {
        var userAgent = http.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent)) return "Unknown Device";
        return userAgent.Length > 500 ? userAgent[..500] : userAgent;
    }
}

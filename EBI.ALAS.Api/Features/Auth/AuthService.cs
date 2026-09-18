using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EBI.ALAS.Api.Common.Time;
using Microsoft.Extensions.Logging;

namespace EBI.ALAS.Api.Features.Auth;

public class AuthService : IAuthService
{
    private const string RefreshTokenCookieName = "refreshToken";
    private const string XsrfCookieName = "XSRF-TOKEN";

    private readonly IAuthRepository _authRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ITokenRevocationRepository _tokenRevocationRepository;
    private readonly IConfiguration _configuration;
    private readonly ITimeProvider _timeProvider;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IAuthRepository authRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IRefreshTokenRepository refreshTokenRepository,
        ITokenRevocationRepository tokenRevocationRepository,
        IConfiguration configuration,
        ITimeProvider timeProvider,
        ILogger<AuthService> logger)
    {
        _authRepository = authRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _refreshTokenRepository = refreshTokenRepository;
        _tokenRevocationRepository = tokenRevocationRepository;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, HttpContext http, CancellationToken ct = default)
    {
        var user = await _authRepository.GetUserByUsernameAsync(request.Username);

        // Timing-attack mitigation: always hash even if user not found
        var dummyHash = BCrypt.Net.BCrypt.HashPassword("dummy_password");
        var passwordHash = user?.PasswordHash ?? dummyHash;
        var isPasswordValid = _passwordHasher.VerifyPassword(request.Password, passwordHash);

        if (user == null || !isPasswordValid || !user.IsActive)
        {
            _logger.LogWarning("Failed login attempt for username: {Username}", request.Username);
            return AuthResult.FailureResult("Invalid credentials");
        }

        var jwtSettings = _configuration.GetSection("Jwt").Get<JwtSettings>()!;
        var rawRefreshToken = _jwtTokenService.GenerateRefreshToken();
        var refreshTokenHash = _jwtTokenService.HashRefreshToken(rawRefreshToken);
        var refreshExpiry = _timeProvider.UtcNow.AddDays(jwtSettings.RefreshTokenExpiryDays);
        var absoluteExpiry = _timeProvider.UtcNow.AddDays(jwtSettings.AbsoluteSessionExpiryDays);
        var deviceInfo = GetDeviceInfo(http);

        // Create the refresh token row first so we have its Id for the `sid` claim
        var refreshToken = await _refreshTokenRepository.CreateRefreshTokenAsync(
            user.Id, refreshTokenHash, refreshExpiry, absoluteExpiry, deviceInfo);

        var (accessToken, xsrfToken) = _jwtTokenService.GenerateTokenWithXsrf(user, refreshToken.Id);
        var accessExpiresAt = _timeProvider.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);

        _logger.LogInformation("User {Username} logged in successfully", user.Username);

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
            _logger.LogDebug("Refresh endpoint called without refresh token cookie");
            return AuthResult.FailureResult("No refresh token provided");
        }

        var tokenHash = _jwtTokenService.HashRefreshToken(rawRefreshToken);
        var storedToken = await _refreshTokenRepository.GetActiveTokenByHashAsync(tokenHash);

        if (storedToken == null)
        {
            _logger.LogWarning("Refresh token not found, revoked, or expired");
            return AuthResult.FailureResult("Invalid refresh token");
        }

        var user = await _authRepository.GetUserByIdAsync(storedToken.UserId);

        if (user == null || !user.IsActive)
        {
            _logger.LogWarning("Refresh token belongs to inactive or missing user {UserId}", storedToken.UserId);
            return AuthResult.FailureResult("User not found or inactive");
        }

        var jwtSettings = _configuration.GetSection("Jwt").Get<JwtSettings>()!;
        var currentJti = http.User?.FindFirstValue(JwtRegisteredClaimNames.Jti);
        if (!string.IsNullOrEmpty(currentJti) && int.TryParse(http.User?.FindFirstValue("userId"), out var currentUserId))
        {
            var currentExpiry = _timeProvider.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);
            await _tokenRevocationRepository.RevokeTokenAsync(currentJti, currentUserId, currentExpiry);
        }

        var newDeviceInfo = GetDeviceInfo(http);

        // Generate the new refresh token values
        var newRawRefreshToken = _jwtTokenService.GenerateRefreshToken();
        var newRefreshTokenHash = _jwtTokenService.HashRefreshToken(newRawRefreshToken);
        var newRefreshExpiry = _timeProvider.UtcNow.AddDays(jwtSettings.RefreshTokenExpiryDays);
        var newAbsoluteExpiry = _timeProvider.UtcNow.AddDays(jwtSettings.AbsoluteSessionExpiryDays);

        // Create the new refresh token row first so we have its Id for the `sid` claim
        var newRefreshToken = await _refreshTokenRepository.CreateRefreshTokenAsync(
            user.Id, newRefreshTokenHash, newRefreshExpiry, newAbsoluteExpiry, newDeviceInfo);

        var (newAccessToken, newXsrfToken) = _jwtTokenService.GenerateTokenWithXsrf(user, newRefreshToken.Id);
        var newAccessExpiresAt = _timeProvider.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);

        _logger.LogInformation("Token refreshed for user {UserId}", user.Id);

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

        int.TryParse(userIdClaim, out var userId);

        if (!string.IsNullOrEmpty(tokenId) && userId > 0)
        {
            var jwtSettings = _configuration.GetSection("Jwt").Get<JwtSettings>()!;
            var expiresAt = DateTime.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);
            await _tokenRevocationRepository.RevokeTokenAsync(tokenId, userId, expiresAt);
            _logger.LogInformation("Access token {TokenId} revoked for user {UserId}", tokenId, userId);
        }

        if (http.Request.Cookies.TryGetValue(RefreshTokenCookieName, out var rawRefreshToken) && !string.IsNullOrEmpty(rawRefreshToken))
        {
            var tokenHash = _jwtTokenService.HashRefreshToken(rawRefreshToken);
            await _refreshTokenRepository.RevokeTokenAsync(tokenHash);
            _logger.LogInformation("Refresh token revoked for user {UserId}", userIdClaim ?? "unknown");
        }
    }

    public async Task ChangePasswordAsync(ClaimsPrincipal principal, ChangePasswordRequest request, HttpContext http, CancellationToken ct = default)
    {
        var userIdClaim = principal.FindFirstValue("userId");
        if (!int.TryParse(userIdClaim, out var userId))
            throw new UnauthorizedAccessException("Invalid user ID");

        var user = await _authRepository.GetUserByIdAsync(userId);
        if (user == null || !user.IsActive)
            throw new UnauthorizedAccessException("User not found or inactive");

        if (!_passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            throw new InvalidOperationException("Current password is incorrect");

        if (request.CurrentPassword == request.NewPassword)
            throw new InvalidOperationException("New password must be different from current password");

        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.MustChangePassword = false;
        await _authRepository.UpdateUserAsync(user);

        await _refreshTokenRepository.RevokeAllUserTokensAsync(userId);
        var tokenId = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
        if (!string.IsNullOrEmpty(tokenId))
        {
            var jwtSettings = _configuration.GetSection("Jwt").Get<JwtSettings>()!;
            var expiresAt = DateTime.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);
            await _tokenRevocationRepository.RevokeTokenAsync(tokenId, userId, expiresAt);
        }

        _logger.LogInformation("User {UserId} changed password successfully", userId);
    }

    private static string GetDeviceInfo(HttpContext http)
    {
        var userAgent = http.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent)) return "Unknown Device";
        return userAgent.Length > 500 ? userAgent[..500] : userAgent;
    }
}

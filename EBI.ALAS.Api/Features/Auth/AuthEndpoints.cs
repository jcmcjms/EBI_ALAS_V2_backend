using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Common.Time;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Authentication endpoint definitions using Minimal APIs.
/// Access token: returned in JSON body, stored in-memory on frontend (Zustand).
/// Refresh token: stored in HttpOnly cookie — invisible to JavaScript, XSS-proof.
/// </summary>
public static class AuthEndpoints
{
    private const string RefreshTokenCookieName = "refreshToken";

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Authentication");

        // ─────────────────────────────────────────────────────────────────
        // POST /api/auth/login
        // Authenticates the user, sets refresh token as HttpOnly cookie,
        // returns access token in JSON body.
        // ─────────────────────────────────────────────────────────────────
        group.MapPost("/login", async (
            [FromBody] LoginRequest request,
            IValidator<LoginRequest> validator,
            IAuthRepository authRepository,
            IPasswordHasher passwordHasher,
            IJwtTokenService jwtTokenService,
            IRefreshTokenRepository refreshTokenRepository,
            IConfiguration configuration,
            HttpContext http,
            ILogger<Program> logger,
            ITimeProvider timeProvider) =>
        {
            // Validate request
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray());

                return Results.BadRequest(ApiResponse.ErrorResponse(
                    "Validation failed",
                    errors.SelectMany(e => e.Value).ToList()));
            }

            // Get user by username
            var user = await authRepository.GetUserByUsernameAsync(request.Username);

            // Timing attack prevention: Always verify password even if user is null
            var dummyHash = BCrypt.Net.BCrypt.HashPassword("dummy_password");
            var passwordHash = user?.PasswordHash ?? dummyHash;
            var isPasswordValid = passwordHasher.VerifyPassword(request.Password, passwordHash);

            if (user == null || !isPasswordValid || !user.IsActive)
            {
                logger.LogWarning("Failed login attempt for username: {Username}", request.Username);
                return Results.Unauthorized();
            }

            // ── Generate Access Token ──────────────────────────────────
            var accessToken = jwtTokenService.GenerateToken(user);
            var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;
            var accessExpiresAt = timeProvider.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);

            // ── Generate Refresh Token ─────────────────────────────────
            var rawRefreshToken = jwtTokenService.GenerateRefreshToken();
            var refreshTokenHash = jwtTokenService.HashRefreshToken(rawRefreshToken);

            var refreshExpiry = timeProvider.UtcNow.AddDays(jwtSettings.RefreshTokenExpiryDays);
            var absoluteExpiry = timeProvider.UtcNow.AddDays(jwtSettings.AbsoluteSessionExpiryDays);

            // Store hashed refresh token in database
            await refreshTokenRepository.CreateRefreshTokenAsync(
                user.Id, refreshTokenHash, refreshExpiry, absoluteExpiry);

            // ── Set HttpOnly Cookie ────────────────────────────────────
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,                                          // Invisible to JavaScript (XSS-proof)
                Secure = true,                                            // HTTPS only in production
                SameSite = SameSiteMode.Strict,                           // CSRF protection (same-origin only)
                Expires = refreshExpiry,                                  // Browser auto-expires
                Path = "/api/auth",                                       // Scoped to auth endpoints only
                IsEssential = true                                        // GDPR: consent not required
            };

            // In Development over plain HTTP, Secure must be false
            if (http.Request.Host.Host == "localhost" || http.Request.Host.Host == "127.0.0.1")
            {
                cookieOptions.Secure = false;
            }

            http.Response.Cookies.Append(RefreshTokenCookieName, rawRefreshToken, cookieOptions);

            logger.LogInformation("User {Username} logged in successfully", user.Username);

            return Results.Ok(ApiResponse<LoginResponse>.SuccessResponse(
                new LoginResponse
                {
                    AccessToken = accessToken,
                    ExpiresAt = accessExpiresAt
                },
                "Login successful"));
        })
        .WithName("Login")
        .Produces<ApiResponse<LoginResponse>>(200)
        .Produces<ApiResponse>(401)
        .Produces<ApiResponse>(400)
        .RequireRateLimiting("LoginLimiter");

        // ─────────────────────────────────────────────────────────────────
        // POST /api/auth/refresh
        // Silent token refresh: reads refresh token from HttpOnly cookie,
        // validates, rotates (revokes old + issues new), returns new access token.
        // Called by frontend useAuthInit on mount and by Axios interceptor on 401.
        // ─────────────────────────────────────────────────────────────────
        group.MapPost("/refresh", async (
            HttpContext http,
            IAuthRepository authRepository,
            IJwtTokenService jwtTokenService,
            IRefreshTokenRepository refreshTokenRepository,
            ITokenRevocationRepository tokenRevocationRepository,
            IConfiguration configuration,
            ILogger<Program> logger,
            ITimeProvider timeProvider) =>
        {
            // ── Read refresh token from HttpOnly cookie ────────────────
            if (!http.Request.Cookies.TryGetValue(RefreshTokenCookieName, out var rawRefreshToken)
                || string.IsNullOrEmpty(rawRefreshToken))
            {
                logger.LogDebug("Refresh endpoint called without refresh token cookie");
                return Results.Unauthorized();
            }

            // ── Validate against database ──────────────────────────────
            var tokenHash = jwtTokenService.HashRefreshToken(rawRefreshToken);
            var storedToken = await refreshTokenRepository.GetActiveTokenByHashAsync(tokenHash);

            if (storedToken == null)
            {
                logger.LogWarning("Refresh token not found, revoked, or expired");
                return Results.Unauthorized();
            }

            // ── Resolve user for new access token ─────────────────────
            var user = await authRepository.GetUserByIdAsync(storedToken.UserId);

            if (user == null || !user.IsActive)
            {
                logger.LogWarning("Refresh token belongs to inactive or missing user {UserId}", storedToken.UserId);
                return Results.Unauthorized();
            }

            // ── Rotate: Revoke old, issue new ─────────────────────────
            // Also revoke the current access token JTI if present (optional extra security)
            // This ensures the old access token can't be used after refresh
            var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;
            var currentJti = http.User?.FindFirstValue(JwtRegisteredClaimNames.Jti);
            if (!string.IsNullOrEmpty(currentJti) && int.TryParse(http.User?.FindFirstValue("userId"), out var currentUserId))
            {
                var currentExpiry = timeProvider.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);
                await tokenRevocationRepository.RevokeTokenAsync(currentJti, currentUserId, currentExpiry);
            }

            // Issue new access token
            var newAccessToken = jwtTokenService.GenerateToken(user);
            var newAccessExpiresAt = timeProvider.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);

            // Issue new refresh token
            var newRawRefreshToken = jwtTokenService.GenerateRefreshToken();
            var newRefreshTokenHash = jwtTokenService.HashRefreshToken(newRawRefreshToken);

            var newRefreshExpiry = timeProvider.UtcNow.AddDays(jwtSettings.RefreshTokenExpiryDays);
            var newAbsoluteExpiry = timeProvider.UtcNow.AddDays(jwtSettings.AbsoluteSessionExpiryDays);

            await refreshTokenRepository.CreateRefreshTokenAsync(
                user.Id, newRefreshTokenHash, newRefreshExpiry, newAbsoluteExpiry);

            // ── Set new HttpOnly cookie ────────────────────────────────
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = newRefreshExpiry,
                Path = "/api/auth",
                IsEssential = true
            };

            if (http.Request.Host.Host == "localhost" || http.Request.Host.Host == "127.0.0.1")
            {
                cookieOptions.Secure = false;
            }

            http.Response.Cookies.Append(RefreshTokenCookieName, newRawRefreshToken, cookieOptions);

            logger.LogInformation("Token refreshed for user {UserId}", user.Id);

            return Results.Ok(ApiResponse<LoginResponse>.SuccessResponse(
                new LoginResponse
                {
                    AccessToken = newAccessToken,
                    ExpiresAt = newAccessExpiresAt
                },
                "Token refreshed successfully"));
        })
        .WithName("RefreshToken")
        .Produces<ApiResponse<LoginResponse>>(200)
        .Produces<ApiResponse>(401);

        // ─────────────────────────────────────────────────────────────────
        // POST /api/auth/logout
        // Revokes both access token (blacklist) and refresh token (DB),
        // then clears the HttpOnly cookie.
        // ─────────────────────────────────────────────────────────────────
        group.MapPost("/logout", async (
            ClaimsPrincipal user,
            HttpContext http,
            ITokenRevocationRepository tokenRevocationRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IJwtTokenService jwtTokenService,
            IConfiguration configuration,
            ILogger<Program> logger) =>
        {
            // ── Revoke access token (JTI blacklist) ───────────────────
            var tokenId = user.FindFirstValue(JwtRegisteredClaimNames.Jti);
            var userIdClaim = user.FindFirstValue("userId");

            if (!string.IsNullOrEmpty(tokenId) && !string.IsNullOrEmpty(userIdClaim)
                && int.TryParse(userIdClaim, out var userId))
            {
                var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;
                var expiresAt = DateTime.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);
                await tokenRevocationRepository.RevokeTokenAsync(tokenId, userId, expiresAt);
                logger.LogInformation("Access token {TokenId} revoked for user {UserId}", tokenId, userId);
            }

            // ── Revoke refresh token (if present in cookie) ───────────
            if (http.Request.Cookies.TryGetValue(RefreshTokenCookieName, out var rawRefreshToken)
                && !string.IsNullOrEmpty(rawRefreshToken))
            {
                var tokenHash = jwtTokenService.HashRefreshToken(rawRefreshToken);
                await refreshTokenRepository.RevokeTokenAsync(tokenHash);
                logger.LogInformation("Refresh token revoked for user {UserId}", userIdClaim ?? "unknown");
            }

            // ── Clear the HttpOnly cookie ──────────────────────────────
            http.Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/api/auth"
            });

            return Results.Ok(ApiResponse.SuccessResponse("Logged out successfully"));
        })
        .WithName("Logout")
        .Produces<ApiResponse>(200)
        .Produces<ApiResponse>(400)
        .RequireAuthorization();

        // ─────────────────────────────────────────────────────────────────
        // POST /api/auth/change-password
        // Requires current password, updates hash, clears MustChangePassword flag,
        // and revokes all active sessions globally (logout everywhere).
        // ─────────────────────────────────────────────────────────────────
        group.MapPost("/change-password", async (
            ClaimsPrincipal principal,
            [FromBody] ChangePasswordRequest request,
            IValidator<ChangePasswordRequest> validator,
            IAuthRepository authRepository,
            IPasswordHasher passwordHasher,
            IRefreshTokenRepository refreshTokenRepository,
            ITokenRevocationRepository tokenRevocationRepository,
            IConfiguration configuration,
            HttpContext http,
            ILogger<Program> logger) =>
        {
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                return Results.BadRequest(ApiResponse.ErrorResponse("Validation failed", errors.SelectMany(e => e.Value).ToList()));
            }

            var userIdClaim = principal.FindFirstValue("userId");
            if (!int.TryParse(userIdClaim, out var userId)) return Results.Unauthorized();

            var user = await authRepository.GetUserByIdAsync(userId);
            if (user == null || !user.IsActive) return Results.Unauthorized();

            if (!passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
                return Results.BadRequest(ApiResponse.ErrorResponse("Current password is incorrect"));

            if (request.CurrentPassword == request.NewPassword)
                return Results.BadRequest(ApiResponse.ErrorResponse("New password must be different from current password"));

            user.PasswordHash = passwordHasher.HashPassword(request.NewPassword);
            user.MustChangePassword = false;
            await authRepository.UpdateUserAsync(user);

            // Revoke all active sessions globally
            await refreshTokenRepository.RevokeAllUserTokensAsync(userId);
            var tokenId = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
            var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;
            var expiresAt = DateTime.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);
            await tokenRevocationRepository.RevokeTokenAsync(tokenId, userId, expiresAt);

            http.Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
            {
                HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/api/auth"
            });

            logger.LogInformation("User {UserId} changed password successfully", userId);
            return Results.Ok(ApiResponse.SuccessResponse("Password changed successfully. Please log in again."));
        })
        .WithName("ChangePassword")
        .Produces<ApiResponse>(200)
        .Produces<ApiResponse>(400)
        .Produces<ApiResponse>(401)
        .RequireAuthorization();
    }
}

/// <summary>
/// Login request DTO.
/// </summary>
public record LoginRequest
{
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

/// <summary>
/// Login response DTO — only the access token is returned.
/// The refresh token lives in the HttpOnly cookie.
/// </summary>
public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// FluentValidation validator for LoginRequest.
/// </summary>
public class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .WithMessage("Username is required")
            .MaximumLength(50)
            .WithMessage("Username must not exceed 50 characters");

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Password is required")
            .MaximumLength(100)
            .WithMessage("Password must not exceed 100 characters");
    }
}

/// <summary>
/// Change password request DTO.
/// </summary>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
/// FluentValidation validator for ChangePasswordRequest.
/// Enforces banking-grade password policy.
/// </summary>
public class ChangePasswordValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty()
            .WithMessage("Current password is required")
            .MaximumLength(100);

        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .WithMessage("New password is required")
            .MinimumLength(8)
            .WithMessage("Password must be at least 8 characters long")
            .MaximumLength(100)
            .Matches("[A-Z]")
            .WithMessage("Must contain an uppercase letter")
            .Matches("[a-z]")
            .WithMessage("Must contain a lowercase letter")
            .Matches("[0-9]")
            .WithMessage("Must contain a digit")
            .Matches(@"[\!\?\*\.]")
            .WithMessage("Must contain one of !? *.");
    }
}

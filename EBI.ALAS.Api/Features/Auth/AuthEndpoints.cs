using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.AuditLogs;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace EBI.ALAS.Api.Features.Auth;

public static class AuthEndpoints
{
    private const string RefreshTokenCookieName = "refreshToken";
    private const string XsrfCookieName = "XSRF-TOKEN";
    private const string XsrfHeaderName = "X-XSRF-TOKEN";

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/login", async (
            [FromBody] LoginRequest request,
            IValidator<LoginRequest> validator,
            IAuthRepository authRepository,
            IPasswordHasher passwordHasher,
            IJwtTokenService jwtTokenService,
            IRefreshTokenRepository refreshTokenRepository,
            IAuditLogService auditLogService,
            IConfiguration configuration,
            HttpContext http,
            ILogger<Program> logger,
            ITimeProvider timeProvider) =>
        {
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                return Results.BadRequest(ApiResponse.ErrorResponse("Validation failed", errors.SelectMany(e => e.Value).ToList()));
            }

            var user = await authRepository.GetUserByUsernameAsync(request.Username);

            var dummyHash = BCrypt.Net.BCrypt.HashPassword("dummy_password");
            var passwordHash = user?.PasswordHash ?? dummyHash;
            var isPasswordValid = passwordHasher.VerifyPassword(request.Password, passwordHash);

            if (user == null || !isPasswordValid || !user.IsActive)
            {
                logger.LogWarning("Failed login attempt for username: {Username}", request.Username);
                return Results.Unauthorized();
            }

            var (accessToken, xsrfToken) = jwtTokenService.GenerateTokenWithXsrf(user);
            var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;
            var accessExpiresAt = timeProvider.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);

            var rawRefreshToken = jwtTokenService.GenerateRefreshToken();
            var refreshTokenHash = jwtTokenService.HashRefreshToken(rawRefreshToken);
            var refreshExpiry = timeProvider.UtcNow.AddDays(jwtSettings.RefreshTokenExpiryDays);
            var absoluteExpiry = timeProvider.UtcNow.AddDays(jwtSettings.AbsoluteSessionExpiryDays);
            var deviceInfo = GetDeviceInfo(http);

            await refreshTokenRepository.CreateRefreshTokenAsync(user.Id, refreshTokenHash, refreshExpiry, absoluteExpiry, deviceInfo);

            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = refreshExpiry,
                Path = "/api/auth",
                IsEssential = true
            };

            if (http.Request.Host.Host == "localhost" || http.Request.Host.Host == "127.0.0.1")
                cookieOptions.Secure = false;

            http.Response.Cookies.Append(RefreshTokenCookieName, rawRefreshToken, cookieOptions);

            var xsrfCookieOptions = new CookieOptions
            {
                HttpOnly = false,
                Secure = cookieOptions.Secure,
                SameSite = SameSiteMode.Strict,
                Expires = accessExpiresAt,
                Path = "/",
                IsEssential = true
            };
            http.Response.Cookies.Append(XsrfCookieName, xsrfToken, xsrfCookieOptions);

            logger.LogInformation("User {Username} logged in successfully", user.Username);

            var userFullName = string.IsNullOrEmpty(user.MiddleName)
                ? $"{user.FirstName} {user.LastName}"
                : $"{user.FirstName} {user.MiddleName} {user.LastName}";
            await auditLogService.LogLoginAsync(user.Id, userFullName, http.Connection.RemoteIpAddress?.ToString() ?? "unknown", GetDeviceInfo(http));

            return Results.Ok(ApiResponse<LoginResponse>.SuccessResponse(new LoginResponse { AccessToken = accessToken, ExpiresAt = accessExpiresAt }, "Login successful"));
        })
        .WithName("Login")
        .Produces<ApiResponse<LoginResponse>>(200)
        .Produces<ApiResponse>(401)
        .Produces<ApiResponse>(400)
        .RequireRateLimiting("LoginLimiter");

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
            if (!http.Request.Cookies.TryGetValue(RefreshTokenCookieName, out var rawRefreshToken) || string.IsNullOrEmpty(rawRefreshToken))
            {
                logger.LogDebug("Refresh endpoint called without refresh token cookie");
                return Results.Unauthorized();
            }

            var tokenHash = jwtTokenService.HashRefreshToken(rawRefreshToken);
            var storedToken = await refreshTokenRepository.GetActiveTokenByHashAsync(tokenHash);

            if (storedToken == null)
            {
                logger.LogWarning("Refresh token not found, revoked, or expired");
                return Results.Unauthorized();
            }

            var user = await authRepository.GetUserByIdAsync(storedToken.UserId);

            if (user == null || !user.IsActive)
            {
                logger.LogWarning("Refresh token belongs to inactive or missing user {UserId}", storedToken.UserId);
                return Results.Unauthorized();
            }

            var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;
            var currentJti = http.User?.FindFirstValue(JwtRegisteredClaimNames.Jti);
            if (!string.IsNullOrEmpty(currentJti) && int.TryParse(http.User?.FindFirstValue("userId"), out var currentUserId))
            {
                var currentExpiry = timeProvider.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);
                await tokenRevocationRepository.RevokeTokenAsync(currentJti, currentUserId, currentExpiry);
            }

            var (newAccessToken, newXsrfToken) = jwtTokenService.GenerateTokenWithXsrf(user);
            var newAccessExpiresAt = timeProvider.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);
            var newRawRefreshToken = jwtTokenService.GenerateRefreshToken();
            var newRefreshTokenHash = jwtTokenService.HashRefreshToken(newRawRefreshToken);
            var newRefreshExpiry = timeProvider.UtcNow.AddDays(jwtSettings.RefreshTokenExpiryDays);
            var newAbsoluteExpiry = timeProvider.UtcNow.AddDays(jwtSettings.AbsoluteSessionExpiryDays);
            var newDeviceInfo = GetDeviceInfo(http);

            await refreshTokenRepository.CreateRefreshTokenAsync(user.Id, newRefreshTokenHash, newRefreshExpiry, newAbsoluteExpiry, newDeviceInfo);

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
                cookieOptions.Secure = false;

            http.Response.Cookies.Append(RefreshTokenCookieName, newRawRefreshToken, cookieOptions);

            var newXsrfCookieOptions = new CookieOptions
            {
                HttpOnly = false,
                Secure = cookieOptions.Secure,
                SameSite = SameSiteMode.Strict,
                Expires = newAccessExpiresAt,
                Path = "/",
                IsEssential = true
            };
            http.Response.Cookies.Append(XsrfCookieName, newXsrfToken, newXsrfCookieOptions);

            logger.LogInformation("Token refreshed for user {UserId}", user.Id);
            return Results.Ok(ApiResponse<LoginResponse>.SuccessResponse(new LoginResponse { AccessToken = newAccessToken, ExpiresAt = newAccessExpiresAt }, "Token refreshed successfully"));
        })
        .WithName("RefreshToken")
        .Produces<ApiResponse<LoginResponse>>(200)
        .Produces<ApiResponse>(401);

        group.MapPost("/logout", async (
            ClaimsPrincipal principal,
            HttpContext http,
            ITokenRevocationRepository tokenRevocationRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IAuditLogService auditLogService,
            IJwtTokenService jwtTokenService,
            IConfiguration configuration,
            ILogger<Program> logger) =>
        {
            var tokenId = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
            var userIdClaim = principal.FindFirstValue("userId");
            var userName = principal.FindFirstValue("username") ?? "unknown";

            int.TryParse(userIdClaim, out var userId);

            if (!string.IsNullOrEmpty(tokenId) && userId > 0)
            {
                var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;
                var expiresAt = DateTime.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);
                await tokenRevocationRepository.RevokeTokenAsync(tokenId, userId, expiresAt);
                logger.LogInformation("Access token {TokenId} revoked for user {UserId}", tokenId, userId);
            }

            if (userId > 0)
            {
                await auditLogService.LogLogoutAsync(userId, userName, http.Connection.RemoteIpAddress?.ToString() ?? "unknown", GetDeviceInfo(http));
            }

            if (http.Request.Cookies.TryGetValue(RefreshTokenCookieName, out var rawRefreshToken) && !string.IsNullOrEmpty(rawRefreshToken))
            {
                var tokenHash = jwtTokenService.HashRefreshToken(rawRefreshToken);
                await refreshTokenRepository.RevokeTokenAsync(tokenHash);
                logger.LogInformation("Refresh token revoked for user {UserId}", userIdClaim ?? "unknown");
            }

            http.Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/api/auth" });
            return Results.Ok(ApiResponse.SuccessResponse("Logged out successfully"));
        })
        .WithName("Logout")
        .Produces<ApiResponse>(200)
        .Produces<ApiResponse>(400)
        .RequireAuthorization();

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
                var errors = validationResult.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
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

            await refreshTokenRepository.RevokeAllUserTokensAsync(userId);
            var tokenId = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
            var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;
            var expiresAt = DateTime.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);
            await tokenRevocationRepository.RevokeTokenAsync(tokenId, userId, expiresAt);

            http.Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/api/auth" });
            logger.LogInformation("User {UserId} changed password successfully", userId);
            return Results.Ok(ApiResponse.SuccessResponse("Password changed successfully. Please log in again."));
        })
        .WithName("ChangePassword")
        .Produces<ApiResponse>(200)
        .Produces<ApiResponse>(400)
        .Produces<ApiResponse>(401)
        .RequireAuthorization();
    }

    private static string GetDeviceInfo(HttpContext http)
    {
        var userAgent = http.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent)) return "Unknown Device";
        return userAgent.Length > 500 ? userAgent[..500] : userAgent;
    }
}

public record LoginRequest { public string Username { get; init; } = string.Empty; public string Password { get; init; } = string.Empty; }

public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Username).NotEmpty().WithMessage("Username is required").MaximumLength(50).WithMessage("Username must not exceed 50 characters");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required").MaximumLength(100).WithMessage("Password must not exceed 100 characters");
    }
}

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public class ChangePasswordValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required").MaximumLength(100);
        RuleFor(x => x.NewPassword).NotEmpty().WithMessage("New password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long")
            .MaximumLength(100)
            .Matches("[A-Z]").WithMessage("Must contain an uppercase letter")
            .Matches("[a-z]").WithMessage("Must contain a lowercase letter")
            .Matches("[0-9]").WithMessage("Must contain a digit")
            .Matches(@"[\!\?\*\.]").WithMessage("Must contain one of !? *.");
    }
}

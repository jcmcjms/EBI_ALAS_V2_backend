using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EBI.ALAS.Api.Common.Models;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Authentication endpoint definitions using Minimal APIs.
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Authentication");

        // POST /api/auth/login
        group.MapPost("/login", async (
            [FromBody] LoginRequest request,
            IValidator<LoginRequest> validator,
            IAuthRepository authRepository,
            IPasswordHasher passwordHasher,
            IJwtTokenService jwtTokenService,
            ILogger<Program> logger) =>
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

            // Generate JWT token
            var token = jwtTokenService.GenerateToken(user);
            var expiresAt = DateTime.UtcNow.AddMinutes(15); // Match JWT expiry

            logger.LogInformation("User {Username} logged in successfully", user.Username);

            return Results.Ok(ApiResponse<LoginResponse>.SuccessResponse(
                new LoginResponse
                {
                    Token = token,
                    ExpiresAt = expiresAt
                },
                "Login successful"));
        })
        .WithName("Login")
        .Produces<ApiResponse<LoginResponse>>(200)
        .Produces<ApiResponse>(401)
        .Produces<ApiResponse>(400)
        .RequireRateLimiting("LoginLimiter");

        // POST /api/auth/logout
        group.MapPost("/logout", async (
            ClaimsPrincipal user,
            ITokenRevocationRepository tokenRevocationRepository,
            IJwtTokenService jwtTokenService,
            ILogger<Program> logger) =>
        {
            // Extract the JTI from the current token
            var tokenId = user.FindFirstValue(JwtRegisteredClaimNames.Jti);
            var userIdClaim = user.FindFirstValue("userId");

            if (string.IsNullOrEmpty(tokenId) || string.IsNullOrEmpty(userIdClaim))
            {
                return Results.BadRequest(ApiResponse.ErrorResponse("Invalid token"));
            }

            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Results.BadRequest(ApiResponse.ErrorResponse("Invalid user ID in token"));
            }

            // Token expires in 15 minutes (or 60 in dev) — blacklist until then
            var expiresAt = DateTime.UtcNow.AddMinutes(15);

            await tokenRevocationRepository.RevokeTokenAsync(tokenId, userId, expiresAt);

            logger.LogInformation("User {UserId} logged out, token {TokenId} revoked", userId, tokenId);

            return Results.Ok(ApiResponse.SuccessResponse("Logged out successfully"));
        })
        .WithName("Logout")
        .Produces<ApiResponse>(200)
        .Produces<ApiResponse>(400)
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
/// Login response DTO.
/// </summary>
public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
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

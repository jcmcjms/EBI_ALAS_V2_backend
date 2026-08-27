using System.Security.Claims;

namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Interface for JWT token generation and validation.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Generate a short-lived JWT access token for the given user.
    /// </summary>
    string GenerateToken(User user);

    /// <summary>
    /// Generate a cryptographically secure random refresh token string.
    /// The raw token is returned once; only its hash should be stored.
    /// </summary>
    string GenerateRefreshToken();

    /// <summary>
    /// Compute the SHA-256 hash of a refresh token for secure storage.
    /// </summary>
    string HashRefreshToken(string refreshToken);

    /// <summary>
    /// Validate a JWT token and return its claims principal.
    /// </summary>
    ClaimsPrincipal? ValidateToken(string token);
}

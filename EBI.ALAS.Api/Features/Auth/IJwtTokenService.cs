using System.Security.Claims;

namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Interface for JWT token generation and validation.
/// </summary>
public interface IJwtTokenService
{
    string GenerateToken(User user);
    ClaimsPrincipal? ValidateToken(string token);
}

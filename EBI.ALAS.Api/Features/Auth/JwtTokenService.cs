using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Time;
using Microsoft.IdentityModel.Tokens;

namespace EBI.ALAS.Api.Features.Auth;

public class JwtTokenService : IJwtTokenService
{
    public const string XsrfTokenClaim = "XsrfToken";

    private readonly IConfiguration _configuration;
    private readonly ILogger<JwtTokenService> _logger;
    private readonly ITimeProvider _timeProvider;

    public JwtTokenService(IConfiguration configuration, ILogger<JwtTokenService> logger, ITimeProvider timeProvider)
    {
        _configuration = configuration;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public string GenerateToken(User user)
    {
        var (accessToken, _) = GenerateTokenWithXsrf(user);
        return accessToken;
    }

    public (string AccessToken, string XsrfToken) GenerateTokenWithXsrf(User user)
    {
        var jwtSettings = _configuration.GetSection("Jwt").Get<JwtSettings>()!;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Get permissions for the user's role
        var permissions = RolePermissions.GetPermissionsForRole(user.Role);

        // Generate a per-session CSRF token. The raw value is mirrored in
        // an XSRF-TOKEN cookie at the login endpoint; this claim lets the
        // CSRF middleware compare the inbound header against the value bound
        // to the user's session.
        var xsrfToken = GenerateXsrfToken();

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, _timeProvider.UtcNowOffset.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim("userId", user.Id.ToString()),
            new Claim("username", user.Username),
            new Claim("firstName", user.FirstName),
            new Claim("lastName", user.LastName),
            new Claim("branchId", user.BranchId),
            new Claim("role", user.Role),
            new Claim("mustChangePassword", user.MustChangePassword.ToString().ToLower()),
            new Claim(XsrfTokenClaim, xsrfToken)
        };

        // Add middle name if present
        if (!string.IsNullOrEmpty(user.MiddleName))
        {
            claims.Add(new Claim("middleName", user.MiddleName));
        }

        // Add permissions
        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        var token = new JwtSecurityToken(
            issuer: jwtSettings.Issuer,
            audience: jwtSettings.Audience,
            claims: claims,
            expires: _timeProvider.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes),
            signingCredentials: credentials
        );

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        return (accessToken, xsrfToken);
    }

    private static string GenerateXsrfToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public string GenerateRefreshToken()
    {
        // Generate 64 cryptographically secure random bytes → 128-char hex string
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToHexString(randomBytes).ToLowerInvariant();
    }

    public string HashRefreshToken(string refreshToken)
    {
        var bytes = Encoding.UTF8.GetBytes(refreshToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var jwtSettings = _configuration.GetSection("Jwt").Get<JwtSettings>()!;
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(jwtSettings.SecretKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid JWT token");
            return null;
        }
    }
}

public class JwtSettings
{
    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 15;
    public int RefreshTokenExpiryDays { get; set; } = 7;
    public int AbsoluteSessionExpiryDays { get; set; } = 14;
}

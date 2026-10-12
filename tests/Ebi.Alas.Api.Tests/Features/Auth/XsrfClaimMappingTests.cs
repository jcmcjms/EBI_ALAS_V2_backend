using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Ebi.Alas.Api.Composition;
using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Users.Domain;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ebi.Alas.Api.Tests.Features.Auth;

/// <summary>
/// Diagnostic: confirm the XsrfToken claim written into the access JWT is still
/// addressable as "XsrfToken" after JwtBearer-style token validation.
/// </summary>
public sealed class XsrfClaimMappingTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "ebi-alas",
        Audience = "alas-clients",
        SigningKey = "unit-test-signing-key-at-least-32-chars!",
        AccessTokenMinutes = 15
    };

    private static User Admin() =>
        User.Create("ada", "hash", "Ada", "Lovelace", "", "ada@example.com", "011", UserRole.Admin, DateTimeOffset.UtcNow);

    private static TokenValidationParameters Parameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Options.Issuer,
        ValidateAudience = true,
        ValidAudience = Options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Options.SigningKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };

    [Fact]
    public void RawJwt_Payload_Contains_XsrfToken_Claim()
    {
        var access = new JwtTokenService(Options)
            .CreateAccessToken(Admin(), "jti-raw", DateTimeOffset.UtcNow);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(access.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "XsrfToken" && !string.IsNullOrWhiteSpace(c.Value));
    }

    [Fact]
    public async Task JsonWebTokenHandler_WithMapInboundClaims_Preserves_XsrfToken_Type()
    {
        var access = new JwtTokenService(Options)
            .CreateAccessToken(Admin(), "jti-json", DateTimeOffset.UtcNow);

        var handler = new JsonWebTokenHandler
        {
            MapInboundClaims = true
        };

        var result = await handler.ValidateTokenAsync(access.Token, Parameters());
        Assert.True(result.IsValid);

        var claimTypes = result.ClaimsIdentity.Claims.Select(c => c.Type).ToArray();
        var xsrf = result.ClaimsIdentity.FindFirst("XsrfToken")?.Value;

        Assert.True(
            xsrf is not null,
            $"XsrfToken claim missing after JsonWebTokenHandler MapInboundClaims=true. Claim types: [{string.Join(", ", claimTypes)}]");
    }

    [Fact]
    public async Task JsonWebTokenHandler_WithoutMapInboundClaims_Preserves_XsrfToken_Type()
    {
        var access = new JwtTokenService(Options)
            .CreateAccessToken(Admin(), "jti-json-raw", DateTimeOffset.UtcNow);

        var handler = new JsonWebTokenHandler
        {
            MapInboundClaims = false
        };

        var result = await handler.ValidateTokenAsync(access.Token, Parameters());
        Assert.True(result.IsValid);

        var claimTypes = result.ClaimsIdentity.Claims.Select(c => c.Type).ToArray();
        var xsrf = result.ClaimsIdentity.FindFirst("XsrfToken")?.Value;

        Assert.True(
            xsrf is not null,
            $"XsrfToken claim missing after JsonWebTokenHandler MapInboundClaims=false. Claim types: [{string.Join(", ", claimTypes)}]");
    }

    [Fact]
    public void JwtSecurityTokenHandler_Validation_Preserves_XsrfToken_Type()
    {
        var access = new JwtTokenService(Options)
            .CreateAccessToken(Admin(), "jti-legacy", DateTimeOffset.UtcNow);

        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = true
        };

        var principal = handler.ValidateToken(access.Token, Parameters(), out _);

        var claimTypes = principal.Claims.Select(c => c.Type).ToArray();
        var xsrf = principal.FindFirst("XsrfToken")?.Value;

        Assert.True(
            xsrf is not null,
            $"XsrfToken claim missing after JwtSecurityTokenHandler MapInboundClaims=true. Claim types: [{string.Join(", ", claimTypes)}]");
    }
}

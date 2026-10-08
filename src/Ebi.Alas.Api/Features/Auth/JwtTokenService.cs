using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Ebi.Alas.Api.Composition;
using Ebi.Alas.Api.Features.Roles;
using Ebi.Alas.Api.Features.Users.Domain;
using Microsoft.IdentityModel.Tokens;

namespace Ebi.Alas.Api.Features.Auth;

public sealed class JwtTokenService(JwtOptions options)
{
    public AccessTokenResult CreateAccessToken(User user, string jti, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(jti);

        var expires = now.AddMinutes(options.AccessTokenMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var permissions = RoleCatalog.All
            .FirstOrDefault(r => r.Role == user.Role.ToString())
            ?.Permissions ?? [];

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new(JwtRegisteredClaimNames.Jti, jti),
            new("role", user.Role.ToString()),
            new("branch", user.BranchId),
            new("fullName", user.FullName),
            new("mustChangePassword", user.MustChangePassword ? "true" : "false"),
            new("XsrfToken", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))),
        ];
        claims.AddRange(permissions.Select(p => new Claim("permission", p)));

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessTokenResult(encoded, expires, jti);
    }
}

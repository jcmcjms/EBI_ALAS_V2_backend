using System.IdentityModel.Tokens.Jwt;
using Ebi.Alas.Api.Composition;
using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Users.Domain;

namespace Ebi.Alas.Api.Tests.Features.Auth;

public sealed class JwtTokenServiceTests
{
    private static JwtTokenService Service() => new(new JwtOptions
    {
        Issuer = "ebi-alas",
        Audience = "alas-clients",
        SigningKey = "unit-test-signing-key-at-least-32-chars!",
        AccessTokenMinutes = 15
    });

    private static User Admin() =>
        User.Create(
            "ada",
            "hash",
            "Ada Lovelace",
            "ada@example.com",
            "011",
            UserRole.Admin,
            DateTimeOffset.UtcNow);

    [Fact]
    public void CreateAccessToken_ContainsRoleAndJtiClaims()
    {
        var result = Service().CreateAccessToken(Admin(), jti: "jti-1", now: DateTimeOffset.UtcNow);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Jti && c.Value == "jti-1");
        Assert.Contains(jwt.Claims, c => c.Type == "role" && c.Value == nameof(UserRole.Admin));
    }

    [Fact]
    public void CreateAccessToken_ContainsUserIdBranchAndFullNameClaims()
    {
        var user = Admin();

        var result = Service().CreateAccessToken(user, jti: "jti-1", now: DateTimeOffset.UtcNow);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == user.Id.ToString());
        Assert.Contains(jwt.Claims, c => c.Type == "branch" && c.Value == "011");
        Assert.Contains(jwt.Claims, c => c.Type == "fullName" && c.Value == "Ada Lovelace");
        Assert.Contains(jwt.Claims, c => c.Type == "unique_name" && c.Value == "ada");
    }

    [Fact]
    public void CreateAccessToken_EmitsPermissionClaimsFromRoleCatalog()
    {
        var result = Service().CreateAccessToken(Admin(), jti: "jti-1", now: DateTimeOffset.UtcNow);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        var permissions = jwt.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToArray();

        Assert.Contains("loans.view", permissions);
        Assert.Contains("loans.manage", permissions);
    }

    [Fact]
    public void CreateAccessToken_IncludesMustChangePasswordClaim()
    {
        var user = Admin();

        var result = Service().CreateAccessToken(user, jti: "jti-1", now: DateTimeOffset.UtcNow);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "mustChangePassword" && c.Value == "true");
    }

    [Fact]
    public void CreateAccessToken_IncludesXsrfTokenClaim_ForClientCsrfHeader()
    {
        var first = Service().CreateAccessToken(Admin(), jti: "jti-1", now: DateTimeOffset.UtcNow);
        var second = Service().CreateAccessToken(Admin(), jti: "jti-2", now: DateTimeOffset.UtcNow);

        var jwt1 = new JwtSecurityTokenHandler().ReadJwtToken(first.Token);
        var jwt2 = new JwtSecurityTokenHandler().ReadJwtToken(second.Token);
        var xsrf1 = jwt1.Claims.Single(c => c.Type == "XsrfToken").Value;
        var xsrf2 = jwt2.Claims.Single(c => c.Type == "XsrfToken").Value;

        Assert.False(string.IsNullOrWhiteSpace(xsrf1));
        Assert.NotEqual(xsrf1, xsrf2);
    }
}

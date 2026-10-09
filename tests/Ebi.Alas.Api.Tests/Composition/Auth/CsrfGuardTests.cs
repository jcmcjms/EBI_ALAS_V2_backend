using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;

namespace Ebi.Alas.Api.Tests.Composition.Auth;

public sealed class CsrfGuardTests
{
    private static ClaimsPrincipal PrincipalWithXsrf(string xsrf)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new("role", "Admin"),
            new("branch", "011"),
            new("XsrfToken", xsrf),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    [Fact]
    public void Allows_Get_WithoutHeader()
    {
        Assert.False(CsrfGuard.IsBlocked("GET", PrincipalWithXsrf("abc"), headerValue: null));
    }

    [Fact]
    public void Blocks_Post_WhenHeaderMissing()
    {
        Assert.True(CsrfGuard.IsBlocked("POST", PrincipalWithXsrf("abc"), headerValue: null));
    }

    [Fact]
    public void Blocks_Post_WhenHeaderDoesNotMatchClaim()
    {
        Assert.True(CsrfGuard.IsBlocked("POST", PrincipalWithXsrf("abc"), headerValue: "xyz"));
    }

    [Fact]
    public void Allows_Post_WhenHeaderMatchesClaim()
    {
        Assert.False(CsrfGuard.IsBlocked("POST", PrincipalWithXsrf("abc"), headerValue: "abc"));
    }

    [Fact]
    public void Blocks_Post_WhenClaimMissing()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) },
            "Test"));

        Assert.True(CsrfGuard.IsBlocked("POST", user, headerValue: "abc"));
    }
}

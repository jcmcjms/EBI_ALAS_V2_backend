using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;

namespace Ebi.Alas.Api.Tests.Composition.Auth;

public sealed class MustChangePasswordGateTests
{
    private static ClaimsPrincipal PrincipalWithMustChange(bool mustChange)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new("role", "Admin"),
            new("branch", "011"),
            new("mustChangePassword", mustChange ? "true" : "false"),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    [Fact]
    public void IsBlocked_WhenMustChange_AndPathIsProtectedApi()
    {
        var user = PrincipalWithMustChange(true);

        Assert.True(MustChangePasswordGate.IsBlocked(user, "POST", "/api/loans"));
    }

    [Fact]
    public void Allows_ChangePassword_WhenMustChange()
    {
        var user = PrincipalWithMustChange(true);

        Assert.False(MustChangePasswordGate.IsBlocked(user, "POST", "/api/auth/change-password"));
    }

    [Fact]
    public void Allows_Logout_WhenMustChange()
    {
        var user = PrincipalWithMustChange(true);

        Assert.False(MustChangePasswordGate.IsBlocked(user, "POST", "/api/auth/logout"));
    }

    [Fact]
    public void Allows_AnyApi_WhenMustChangeIsFalse()
    {
        var user = PrincipalWithMustChange(false);

        Assert.False(MustChangePasswordGate.IsBlocked(user, "GET", "/api/loans"));
    }

    [Fact]
    public void Allows_AnonymousPaths_WhenMustChange()
    {
        var user = PrincipalWithMustChange(true);

        Assert.False(MustChangePasswordGate.IsBlocked(user, "POST", "/api/auth/login"));
        Assert.False(MustChangePasswordGate.IsBlocked(user, "GET", "/health/live"));
    }
}

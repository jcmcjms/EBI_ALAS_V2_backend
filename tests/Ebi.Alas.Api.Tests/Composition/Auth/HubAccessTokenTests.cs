using Ebi.Alas.Api.Composition.Auth;

namespace Ebi.Alas.Api.Tests.Composition.Auth;

public sealed class HubAccessTokenTests
{
    [Fact]
    public void Resolve_HubPath_WithAccessToken_ReturnsToken()
    {
        var token = HubAccessToken.Resolve("/hubs/notifications", "jwt-value");

        Assert.Equal("jwt-value", token);
    }

    [Fact]
    public void Resolve_HubPath_WithoutAccessToken_ReturnsNull()
    {
        var token = HubAccessToken.Resolve("/hubs/notifications", null);

        Assert.Null(token);
    }

    [Fact]
    public void Resolve_NonHubPath_WithAccessToken_ReturnsNull()
    {
        var token = HubAccessToken.Resolve("/api/notifications", "jwt-value");

        Assert.Null(token);
    }
}

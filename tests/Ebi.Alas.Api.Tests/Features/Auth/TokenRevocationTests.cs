using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ebi.Alas.Api.Tests.Features.Auth;

public sealed class TokenRevocationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TokenRevocationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            Ebi.Alas.Api.Tests.TestHostSecrets.Apply(builder);
        });
    }

    private sealed record LoginBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("accessToken")] string AccessToken);

    private async Task<(HttpClient Client, string AccessToken, string Xsrf)> LoginAsync()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = TestHostSecrets.CurrentAdminUserName,
            password = TestHostSecrets.CurrentAdminPassword
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<LoginBody>();
        Assert.NotNull(body);

        var xsrf = new JwtSecurityTokenHandler()
            .ReadJwtToken(body!.AccessToken)
            .Claims.First(c => c.Type == "XsrfToken")
            .Value;
        Assert.False(string.IsNullOrWhiteSpace(xsrf));
        return (client, body.AccessToken, xsrf);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string accessToken, string? xsrf = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (xsrf is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", xsrf);
        }
        return request;
    }

    /// <summary>
    /// Logout is authorized but remains reachable for a must-change-password principal,
    /// so it doubles as a probe for whether the bearer token still authenticates.
    /// </summary>
    private static async Task<HttpStatusCode> ProbeAsync(HttpClient client, string accessToken, string xsrf)
    {
        using var response = await client.SendAsync(
            Authorized(HttpMethod.Post, "/api/auth/logout", accessToken, xsrf));
        return response.StatusCode;
    }

    [Fact]
    public async Task RevokedAccessToken_IsRejected_OnReuse()
    {
        var (client, accessToken, xsrf) = await LoginAsync();

        Assert.Equal(HttpStatusCode.NoContent, await ProbeAsync(client, accessToken, xsrf));

        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeAsync(client, accessToken, xsrf));
    }

    [Fact]
    public async Task UnrevokedAccessToken_RemainsValid_AfterDifferentTokenRevoked()
    {
        var (clientA, tokenA, xsrfA) = await LoginAsync();
        var (clientB, tokenB, xsrfB) = await LoginAsync();

        Assert.Equal(HttpStatusCode.NoContent, await ProbeAsync(clientA, tokenA, xsrfA));
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeAsync(clientA, tokenA, xsrfA));

        Assert.Equal(HttpStatusCode.NoContent, await ProbeAsync(clientB, tokenB, xsrfB));
    }
}

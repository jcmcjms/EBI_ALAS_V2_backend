using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ebi.Alas.Api.Tests.Api;

public sealed class PresenceApiTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string TemporaryPassword = "PresenceTest!2";

    private readonly WebApplicationFactory<Program> _factory;
    private string _userName = string.Empty;
    private string _originalPassword = string.Empty;
    private string _accessToken = string.Empty;
    private string _xsrf = string.Empty;

    public PresenceApiTests(WebApplicationFactory<Program> factory)
    {
        string? userName = null;
        string? password = null;
        _factory = factory.WithWebHostBuilder(builder =>
        {
            TestHostSecrets.Apply(builder);
            userName = TestHostSecrets.CurrentAdminUserName;
            password = TestHostSecrets.CurrentAdminPassword;
        });
        _ = _factory.CreateClient();
        _userName = userName!;
        _originalPassword = password!;
    }

    public async Task InitializeAsync()
    {
        var client = _factory.CreateClient();
        var (firstToken, firstXsrf) = await LoginAsync(client, _originalPassword);

        using var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new
            {
                currentPassword = _originalPassword,
                newPassword = TemporaryPassword
            })
        };
        change.Headers.Authorization = new AuthenticationHeaderValue("Bearer", firstToken);
        change.Headers.Add("X-XSRF-TOKEN", firstXsrf);
        var changeResponse = await client.SendAsync(change);
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);

        (_accessToken, _xsrf) = await LoginAsync(client, TemporaryPassword);
    }

    public async Task DisposeAsync()
    {
        var client = _factory.CreateClient();
        var (token, xsrf) = await LoginAsync(client, TemporaryPassword);
        using var restore = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new
            {
                currentPassword = TemporaryPassword,
                newPassword = _originalPassword
            })
        };
        restore.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        restore.Headers.Add("X-XSRF-TOKEN", xsrf);
        await client.SendAsync(restore);
    }

    private sealed record LoginBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("accessToken")] string AccessToken);

    private static string? ExtractXsrfClaim(string accessToken)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        return jwt.Claims.FirstOrDefault(c => c.Type == "XsrfToken")?.Value;
    }

    private async Task<(string AccessToken, string Xsrf)> LoginAsync(HttpClient client, string password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = _userName,
            password
        });
        Assert.True(
            login.StatusCode == HttpStatusCode.OK,
            $"Login failed for {_userName}. Status={login.StatusCode}");
        var body = await login.Content.ReadFromJsonAsync<LoginBody>();
        Assert.NotNull(body);
        var xsrf = ExtractXsrfClaim(body!.AccessToken);
        Assert.False(string.IsNullOrWhiteSpace(xsrf));
        return (body.AccessToken, xsrf!);
    }

    [Fact]
    public async Task Online_ReturnsRowsWithNonEmptyCamelCaseName()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        using var heartbeatRequest = new HttpRequestMessage(HttpMethod.Post, "/api/presence/heartbeat");
        heartbeatRequest.Headers.Add("X-XSRF-TOKEN", _xsrf);
        var heartbeat = await client.SendAsync(heartbeatRequest);
        Assert.Equal(HttpStatusCode.NoContent, heartbeat.StatusCode);

        var response = await client.GetAsync("/api/presence/online");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        var row = Assert.Single(doc.RootElement.EnumerateArray());

        Assert.True(
            row.TryGetProperty("name", out var name),
            $"GET /api/presence/online must serialize a camelCase 'name' property. Body={json}");
        Assert.False(
            string.IsNullOrWhiteSpace(name.GetString()),
            $"GET /api/presence/online 'name' must be non-empty. Body={json}");
        Assert.NotEqual(" ", name.GetString());
    }
}

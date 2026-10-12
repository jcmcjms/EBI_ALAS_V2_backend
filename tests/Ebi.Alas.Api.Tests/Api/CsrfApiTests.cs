using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ebi.Alas.Api.Tests.Api;

public sealed class CsrfApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CsrfApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            Ebi.Alas.Api.Tests.TestHostSecrets.Apply(builder);
        });
    }

    private sealed record LoginBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("accessToken")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("mustChangePassword")] bool MustChangePassword);

    private static string? ExtractXsrfClaim(string accessToken)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        return jwt.Claims.FirstOrDefault(c => c.Type == "XsrfToken")?.Value;
    }

    private async Task<LoginBody> LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = TestHostSecrets.CurrentAdminUserName,
            password = TestHostSecrets.CurrentAdminPassword
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginBody>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
        return body;
    }

    [Fact]
    public async Task ChangePassword_WithMatchingXsrfHeader_IsNotRejectedByCsrfGuard()
    {
        var client = _factory.CreateClient();
        var login = await LoginAsync(client);
        var xsrf = ExtractXsrfClaim(login.AccessToken);
        Assert.False(string.IsNullOrWhiteSpace(xsrf));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new
            {
                currentPassword = "not-the-real-password-Aa1!",
                newPassword = "NewPass!2"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        request.Headers.Add("X-XSRF-TOKEN", xsrf);

        var response = await client.SendAsync(request);

        // CSRF guard runs before the handler. 400 = handler rejected the password.
        // 403 with CSRF title = guard blocked the request despite a matching header.
        var body = await response.Content.ReadAsStringAsync();
        Assert.False(
            response.StatusCode == HttpStatusCode.Forbidden && body.Contains("CSRF", StringComparison.OrdinalIgnoreCase),
            $"CSRF guard blocked a request with a matching X-XSRF-TOKEN. Status={response.StatusCode} Body={body}");
    }

    [Fact]
    public async Task ChangePassword_WithoutXsrfHeader_ReturnsCsrf403()
    {
        var client = _factory.CreateClient();
        var login = await LoginAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new
            {
                currentPassword = "whatever-Aa1!",
                newPassword = "NewPass!2"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("X-XSRF-TOKEN", body, StringComparison.OrdinalIgnoreCase);
    }
}

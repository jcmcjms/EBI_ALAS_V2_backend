using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ebi.Alas.Api.Tests.Api;

public sealed class AuthApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            Ebi.Alas.Api.Tests.TestHostSecrets.Apply(builder);
        });
    }

    [Fact]
    public async Task Refresh_WithoutToken_Returns401_Not400()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithEmptyJsonBody_DoesNotReturn400()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync(
            "/api/auth/refresh",
            new StringContent("null", System.Text.Encoding.UTF8, "application/json"));
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_Unauthenticated_Returns401_Not404()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "whatever-Aa1!",
            newPassword = "NewPass!2"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_Failure_DoesNotSetRefreshCookie()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "missing-user",
            password = "wrong"
        });

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(c => c.StartsWith("alas_refresh=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Login_Success_SetsHttpOnlySecureRefreshCookie()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = TestHostSecrets.CurrentAdminUserName,
            password = TestHostSecrets.CurrentAdminPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookies));

        var refresh = setCookies.FirstOrDefault(c => c.StartsWith("alas_refresh=", StringComparison.Ordinal));
        Assert.NotNull(refresh);
        Assert.Contains("HttpOnly", refresh, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", refresh, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", refresh, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", refresh, StringComparison.OrdinalIgnoreCase);
    }
}

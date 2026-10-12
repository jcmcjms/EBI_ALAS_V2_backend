using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ebi.Alas.Api.Tests.Api;

/// <summary>
/// User import upload validation. The seeded admin requires a password change before
/// reaching the endpoint; the change is restored afterwards so the shared in-memory
/// admin account stays usable for other tests.
/// </summary>
public sealed class UserImportApiTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string TemporaryPassword = "ImportTest!2";

    private readonly WebApplicationFactory<Program> _factory;
    private string _userName = string.Empty;
    private string _originalPassword = string.Empty;
    private string _accessToken = string.Empty;
    private string _xsrf = string.Empty;

    public UserImportApiTests(WebApplicationFactory<Program> factory)
    {
        string? userName = null;
        string? password = null;
        _factory = factory.WithWebHostBuilder(builder =>
        {
            TestHostSecrets.Apply(builder);
            userName = TestHostSecrets.CurrentAdminUserName;
            password = TestHostSecrets.CurrentAdminPassword;
        });
        // Force host build so Apply runs and we capture the credentials this host seeds with.
        _ = _factory.CreateClient();
        _userName = userName!;
        _originalPassword = password!;
    }

    public async Task InitializeAsync()
    {
        var client = _factory.CreateClient();
        var (firstToken, firstXsrf) = await LoginAsync(client, _originalPassword);

        // Seeded admin starts with MustChangePassword=true, which blocks /api/users/import.
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
    public async Task Import_WithNonXlsxFileName_Returns400()
    {
        var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "users.csv");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/import")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Headers.Add("X-XSRF-TOKEN", _xsrf);

        var response = await client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected 400 for non-xlsx upload. Status={response.StatusCode} Body={body}");
        Assert.Contains(".xlsx", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Import_WithXlsxExtension_DoesNotFailFileTypeGate()
    {
        var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "users.xlsx");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/import")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Headers.Add("X-XSRF-TOKEN", _xsrf);

        var response = await client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Only .xlsx files are supported", body, StringComparison.OrdinalIgnoreCase);
    }
}

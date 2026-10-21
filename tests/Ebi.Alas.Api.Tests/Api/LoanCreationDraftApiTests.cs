using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ebi.Alas.Api.Tests.Api;

public sealed class LoanCreationDraftApiTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string TemporaryPassword = "CreationDraft-Pw!2";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _userName;
    private string _accessToken = string.Empty;
    private string _xsrf = string.Empty;

    public LoanCreationDraftApiTests(WebApplicationFactory<Program> factory)
    {
        string? userName = null;
        _factory = factory.WithWebHostBuilder(builder =>
        {
            TestHostSecrets.Apply(builder);
            userName = TestHostSecrets.CurrentAdminUserName;
        });
        _ = _factory.CreateClient();
        _userName = userName!;
    }

    public async Task InitializeAsync()
    {
        var client = _factory.CreateClient();
        var (firstToken, firstXsrf) = await LoginAsync(client, TestHostSecrets.CurrentAdminPassword);

        using var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new
            {
                currentPassword = TestHostSecrets.CurrentAdminPassword,
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
                newPassword = TestHostSecrets.CurrentAdminPassword
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

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", _xsrf);
        return client;
    }

    [Fact]
    public async Task CreationDraft_GetEmpty_Returns204()
    {
        var client = AuthenticatedClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlasDbContext>();
        var drafts = await db.LoanCreationDrafts.ToListAsync();
        db.LoanCreationDrafts.RemoveRange(drafts);
        await db.SaveChangesAsync();

        var response = await client.GetAsync("/api/loans/creation-draft");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task CreationDraft_PutGetDelete_RoundTrips()
    {
        var client = AuthenticatedClient();
        const string payload = "{\"client\":{\"cisId\":\"CIS1\"}}";

        var saved = await client.PutAsJsonAsync("/api/loans/creation-draft", new { payload });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using (var doc = JsonDocument.Parse(await saved.Content.ReadAsStringAsync()))
        {
            Assert.Equal(payload, doc.RootElement.GetProperty("payload").GetString());
            Assert.True(doc.RootElement.TryGetProperty("updatedAt", out _));
        }

        var fetched = await client.GetAsync("/api/loans/creation-draft");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        using (var doc = JsonDocument.Parse(await fetched.Content.ReadAsStringAsync()))
        {
            Assert.Equal(payload, doc.RootElement.GetProperty("payload").GetString());
        }

        var deleted = await client.DeleteAsync("/api/loans/creation-draft");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var afterDelete = await client.GetAsync("/api/loans/creation-draft");
        Assert.Equal(HttpStatusCode.NoContent, afterDelete.StatusCode);
    }

    [Fact]
    public async Task CreationDraft_EmptyPayload_DeletesDraft()
    {
        var client = AuthenticatedClient();

        var saved = await client.PutAsJsonAsync("/api/loans/creation-draft", new { payload = "{}" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var cleared = await client.PutAsJsonAsync("/api/loans/creation-draft", new { payload = "  " });
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);

        var afterClear = await client.GetAsync("/api/loans/creation-draft");
        Assert.Equal(HttpStatusCode.NoContent, afterClear.StatusCode);
    }

    [Fact]
    public async Task CreationDraft_OverMaxLength_Returns400()
    {
        var client = AuthenticatedClient();

        var response = await client.PutAsJsonAsync(
            "/api/loans/creation-draft",
            new { payload = new string('x', LoanCreationDraft.MaxPayloadLength + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreationDraft_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/loans/creation-draft");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

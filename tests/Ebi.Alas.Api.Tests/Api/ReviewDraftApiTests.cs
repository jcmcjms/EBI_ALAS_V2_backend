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

public sealed class ReviewDraftApiTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string TemporaryPassword = "DraftTest-Pw!2";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _userName;
    private string _accessToken = string.Empty;
    private string _xsrf = string.Empty;

    public ReviewDraftApiTests(WebApplicationFactory<Program> factory)
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
        // The seeded admin carries mustChangePassword; the gate blocks every other
        // API call until the password is rotated (same pattern as PresenceApiTests).
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

    private Guid SeedLoan(LoanStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlasDbContext>();
        var admin = db.Users.AsNoTracking().First(u => u.Role == UserRole.Admin);
        var loan = LoanApplication.Create(
            $"LAM-DRAFT-{Guid.NewGuid():N}"[..24],
            $"GRP-{Guid.NewGuid():N}"[..12],
            "Draft Test Client",
            "BR-HQ",
            LoanType.New,
            1000m, 30, 0.05m, 10m, 50m, 940m,
            admin.Id, DateTimeOffset.UtcNow);
        if (status != LoanStatus.ForRecommendation)
        {
            loan.TransitionTo(status, DateTimeOffset.UtcNow);
        }

        db.LoanApplications.Add(loan);
        db.SaveChanges();
        return loan.Id;
    }

    [Fact]
    public async Task Recommend_WithoutBody_DoesNotReturn400()
    {
        var client = AuthenticatedClient();
        var response = await client.PostAsync($"/api/loans/{Guid.NewGuid()}/recommend", content: null);

        // Loan does not exist; the point is that the empty body binds instead of 400.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Approve_WithoutBody_DoesNotReturn400()
    {
        var client = AuthenticatedClient();
        var response = await client.PostAsync($"/api/loans/{Guid.NewGuid()}/approve", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReviewDraft_PutGetDelete_RoundTrips()
    {
        var client = AuthenticatedClient();
        var loanId = SeedLoan(LoanStatus.ForChecking);

        var missing = await client.GetAsync($"/api/loans/{loanId}/review-draft");
        Assert.Equal(HttpStatusCode.NoContent, missing.StatusCode);

        var saved = await client.PutAsJsonAsync(
            $"/api/loans/{loanId}/review-draft",
            new { remarks = "  verify COE and payslips  " });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var fetched = await client.GetAsync($"/api/loans/{loanId}/review-draft");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        using (var doc = JsonDocument.Parse(await fetched.Content.ReadAsStringAsync()))
        {
            Assert.Equal("verify COE and payslips", doc.RootElement.GetProperty("remarks").GetString());
            Assert.True(doc.RootElement.TryGetProperty("updatedAt", out _));
        }

        var cleared = await client.PutAsJsonAsync(
            $"/api/loans/{loanId}/review-draft",
            new { remarks = "   " });
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);

        var afterClear = await client.GetAsync($"/api/loans/{loanId}/review-draft");
        Assert.Equal(HttpStatusCode.NoContent, afterClear.StatusCode);
    }

    [Fact]
    public async Task ReviewDraft_OverMaxLength_Returns400()
    {
        var client = AuthenticatedClient();
        var loanId = SeedLoan(LoanStatus.ForChecking);

        var response = await client.PutAsJsonAsync(
            $"/api/loans/{loanId}/review-draft",
            new { remarks = new string('x', ReviewDraft.MaxRemarksLength + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReviewDraft_UnknownLoan_Returns404()
    {
        var client = AuthenticatedClient();

        var response = await client.PutAsJsonAsync(
            $"/api/loans/{Guid.NewGuid()}/review-draft",
            new { remarks = "notes" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReviewDraft_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var loanId = SeedLoan(LoanStatus.ForChecking);

        var response = await client.PutAsJsonAsync(
            $"/api/loans/{loanId}/review-draft",
            new { remarks = "notes" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Evaluate_WithRemarks_RecordsCommentsInHistory()
    {
        var client = AuthenticatedClient();
        var loanId = SeedLoan(LoanStatus.ForChecking);

        var evaluate = await client.PostAsJsonAsync(
            $"/api/loans/{loanId}/evaluate",
            new { recommend = true, remarks = "Capacity verified; recommend approval." });
        Assert.Equal(HttpStatusCode.OK, evaluate.StatusCode);

        var history = await client.GetFromJsonAsync<JsonElement>($"/api/loans/{loanId}/history");
        var entry = history.EnumerateArray().Single();
        Assert.Equal("EvaluatedRecommended", entry.GetProperty("action").GetString());
        Assert.Equal("Capacity verified; recommend approval.", entry.GetProperty("comments").GetString());
    }

    [Fact]
    public async Task Evaluate_WithoutRemarks_IsRejected()
    {
        var client = AuthenticatedClient();
        var loanId = SeedLoan(LoanStatus.ForChecking);

        var evaluate = await client.PostAsJsonAsync(
            $"/api/loans/{loanId}/evaluate",
            new { recommend = true });

        Assert.Equal(HttpStatusCode.BadRequest, evaluate.StatusCode);
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace Ebi.Alas.Api.Tests.Features.WebLoans;

public sealed class WebLoanAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "webloan-auth-tests-signing-key-32chars!";
    private const string Issuer = "test-issuer";
    private const string Audience = "test-clients";

    private readonly WebApplicationFactory<Program> _factory;

    public WebLoanAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            Ebi.Alas.Api.Tests.TestHostSecrets.Apply(builder);
            builder.UseSetting("Api:Jwt:SigningKey", SigningKey);
            builder.UseSetting("Api:Jwt:Issuer", Issuer);
            builder.UseSetting("Api:Jwt:Audience", Audience);
        });
    }

    private static string MintToken(string role, params string[] permissions)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("role", role),
            new("branch", "BR-HQ"),
            new("mustChangePassword", "false"),
        };
        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task CisSearch_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/webloans/cis?q=test");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CisSearch_AuthenticatedWithoutWebLoansView_Returns403()
    {
        var token = MintToken("Encoder", "loans.view");
        using var client = ClientFor(token);

        var response = await client.GetAsync("/api/webloans/cis?q=test");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CisSearch_WithWebLoansViewPermission_Returns200()
    {
        var token = MintToken("Encoder", "webloans.view");
        using var client = ClientFor(token);

        var response = await client.GetAsync("/api/webloans/cis?q=test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CisSearch_AdminRole_Returns200()
    {
        var token = MintToken("Admin");
        using var client = ClientFor(token);

        var response = await client.GetAsync("/api/webloans/cis?q=test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LoanProducts_AuthenticatedWithoutWebLoansView_Returns403()
    {
        var token = MintToken("Recommender", "loans.view", "loans.recommend");
        using var client = ClientFor(token);

        var response = await client.GetAsync("/api/webloans/loan-products");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OutstandingLoans_AuthenticatedWithoutWebLoansView_Returns403()
    {
        var token = MintToken("Evaluator", "loans.view");
        using var client = ClientFor(token);

        var response = await client.GetAsync("/api/webloans/cis/0880914721/accounts/011-05-1/outstanding-loans");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

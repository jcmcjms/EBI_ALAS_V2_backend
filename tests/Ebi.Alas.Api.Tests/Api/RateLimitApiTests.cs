using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ebi.Alas.Api.Tests.Api;

public sealed class RateLimitApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RateLimitApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            Ebi.Alas.Api.Tests.TestHostSecrets.Apply(builder);
        });
    }

    [Fact]
    public async Task Login_BurstEventuallyReturns429()
    {
        var client = _factory.CreateClient();
        var sawTooMany = false;
        for (var i = 0; i < 15; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new
            {
                userName = "missing-user",
                password = "wrong"
            });

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                sawTooMany = true;
                break;
            }
        }

        Assert.True(sawTooMany, "Expected rate limiter to return 429 under login burst.");
    }
}

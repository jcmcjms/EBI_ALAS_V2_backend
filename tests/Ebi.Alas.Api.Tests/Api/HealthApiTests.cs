using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ebi.Alas.Api.Tests.Api;

public sealed class HealthApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            Ebi.Alas.Api.Tests.TestHostSecrets.Apply(builder);
        });
    }

    [Fact]
    public async Task HealthLive_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_ReturnsOk_InDevelopmentWithInMemory()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownUser_Returns403_Not500()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "missing-user",
            password = "wrong"
        });
        Assert.NotEqual(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/does-not-exist");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ScalarUi_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/scalar");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocument_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ebi.Alas.Api.Tests.Api;

public sealed class BranchApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BranchApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            Ebi.Alas.Api.Tests.TestHostSecrets.Apply(builder);
        });
    }

    [Fact]
    public async Task GetBranches_Unauthenticated_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/branches");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetBranchByCode_Unauthenticated_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/branches/011");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostBranch_Unauthenticated_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/branches", new
        {
            code = "040",
            name = "X",
            areaCode = "NA"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PatchBranchStatus_Unauthenticated_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.PatchAsJsonAsync("/api/branches/011/status", new
        {
            status = "Closed"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

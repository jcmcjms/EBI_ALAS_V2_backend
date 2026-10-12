using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ebi.Alas.Api.Tests.Api;

/// <summary>
/// Write rate-limit coverage: mutation endpoints must carry the "write" policy,
/// and a write endpoint must return 429 under burst.
/// </summary>
public sealed class WriteRateLimitApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WriteRateLimitApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            Ebi.Alas.Api.Tests.TestHostSecrets.Apply(builder);
        });
    }

    public static TheoryData<string, string, string> WriteMutationEndpoints => new()
    {
        { "POST", "/api/users", "user create" },
        { "PUT", "/api/users/{id:guid}", "user update" },
        { "POST", "/api/users/{id:guid}/suspend", "user suspend" },
        { "POST", "/api/users/{id:guid}/activate", "user activate" },
        { "POST", "/api/loans", "loan submit" },
        { "POST", "/api/loans/queue/claim", "desk claim" },
        { "POST", "/api/loans/queue/release", "desk release" },
        { "POST", "/api/loans/queue/{loanId:guid}/claim", "desk claim by id" },
        { "POST", "/api/workflow/queue/claim", "workflow claim" },
        { "POST", "/api/workflow/queue/{id:guid}/release", "workflow release" },
        { "POST", "/api/notifications/{id:guid}/read", "notification read" },
        { "POST", "/api/notifications/read-all", "notification read-all" },
        { "POST", "/api/auth/logout", "logout" },
        { "PUT", "/api/loan-products/{code}", "loan product policy update" },
    };

    [Theory]
    [MemberData(nameof(WriteMutationEndpoints))]
    public void MutationEndpoint_HasWriteRateLimitPolicy(string method, string route, string description)
    {
        var endpoint = FindEndpoint(method, route);
        Assert.True(endpoint is not null, $"Endpoint not found for {description}: {method} {route}");

        var rateLimit = endpoint!.Metadata.GetMetadata<EnableRateLimitingAttribute>();
        Assert.True(
            rateLimit is not null && string.Equals(rateLimit.PolicyName, "write", StringComparison.Ordinal),
            $"Expected RequireRateLimiting(\"write\") on {description} ({method} {route}).");
    }

    [Fact]
    public async Task Logout_BurstEventuallyReturns429()
    {
        // Fresh factory so prior write-limit consumption in this class cannot mask a missing policy.
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            Ebi.Alas.Api.Tests.TestHostSecrets.Apply(builder);
        });
        var client = factory.CreateClient();

        var sawTooMany = false;
        for (var i = 0; i < 40; i++)
        {
            var response = await client.PostAsync("/api/auth/logout", content: null);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                sawTooMany = true;
                break;
            }
        }

        Assert.True(sawTooMany, "Expected write rate limiter to return 429 under logout burst.");
    }

    private RouteEndpoint? FindEndpoint(string method, string route)
    {
        var dataSource = _factory.Services.GetRequiredService<EndpointDataSource>();
        foreach (var endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            if (methods is null || !methods.Contains(method, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (RoutesMatch(endpoint.RoutePattern.RawText, route))
            {
                return endpoint;
            }
        }

        return null;
    }

    private static bool RoutesMatch(string? raw, string expected)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        static string Normalize(string value) => value.Trim('/').ToLowerInvariant();
        return string.Equals(Normalize(raw), Normalize(expected), StringComparison.Ordinal);
    }
}

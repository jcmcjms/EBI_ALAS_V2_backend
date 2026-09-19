using System.Threading.RateLimiting;
using EBI.ALAS.Api.Common.Models;
using Microsoft.AspNetCore.RateLimiting;

namespace EBI.ALAS.Api.Common.Extensions;

/// <summary>
/// Rate limiting configuration.
/// Extracted from Program.cs to follow Single Responsibility Principle.
/// </summary>
public static class RateLimitingExtensions
{
    public static IServiceCollection AddBankingRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = HandleRateLimitRejection;

            // Login limiter: 5 requests per 60 seconds (brute-force protection)
            options.AddFixedWindowLimiter("LoginLimiter", limiterOptions =>
            {
                limiterOptions.PermitLimit = configuration.GetValue<int>(
                    "RateLimiting:Login:PermitLimit", 5);
                limiterOptions.Window = TimeSpan.FromSeconds(configuration.GetValue<int>(
                    "RateLimiting:Login:WindowSeconds", 60));
                limiterOptions.QueueLimit = 0;
            });

            // Per-user data limiter: 120 requests per 60 seconds
            options.AddPolicy("DataLimiter", context =>
            {
                var partitionKey = ResolvePartitionKey(context.User, context.Connection);
                return CreateFixedWindowLimiter(configuration, partitionKey);
            });

            // Global limiter (excludes auth and health endpoints)
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var path = context.Request.Path.Value ?? string.Empty;
                if (IsExemptPath(path))
                    return RateLimitPartition.GetNoLimiter("no-limit");

                var partitionKey = ResolvePartitionKey(context.User, context.Connection);
                return CreateFixedWindowLimiter(configuration, partitionKey);
            });
        });

        return services;
    }

    private static async ValueTask HandleRateLimitRejection(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        var payload = ApiResponse.ErrorResponse("Too many requests. Please slow down and retry.");
        await context.HttpContext.Response.WriteAsJsonAsync(payload, cancellationToken);
    }

    private static string ResolvePartitionKey(
        System.Security.Claims.ClaimsPrincipal? user,
        Microsoft.AspNetCore.Http.ConnectionInfo connection)
    {
        if (user?.Identity?.IsAuthenticated == true)
        {
            return user.Identity.Name
                ?? user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                ?? "anonymous";
        }

        return connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
    }

    private static bool IsExemptPath(string path)
    {
        return path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase);
    }

    private static RateLimitPartition<string> CreateFixedWindowLimiter(
        IConfiguration configuration, string partitionKey)
    {
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = configuration.GetValue<int>("RateLimiting:Data:PermitLimit", 120),
            Window = TimeSpan.FromSeconds(configuration.GetValue<int>("RateLimiting:Data:WindowSeconds", 60)),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    }
}

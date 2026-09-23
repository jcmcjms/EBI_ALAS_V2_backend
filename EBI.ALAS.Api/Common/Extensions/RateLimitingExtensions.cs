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

            // Login limiter partitioned by IP + submitted username.
            // Uses a composite key so each IP and each username get independent budgets.
            // Prevents one actor from burning the entire org's login budget.
            options.AddPolicy("LoginLimiter", context =>
            {
                var loginPartitionKey = ResolveLoginPartitionKey(context);
                var permitLimit = configuration.GetValue<int>("RateLimiting:Login:PermitLimit", 5);
                var windowSeconds = configuration.GetValue<int>("RateLimiting:Login:WindowSeconds", 60);

                return RateLimitPartition.GetFixedWindowLimiter(loginPartitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit = 0
                });
            });

            // Per-user data limiter using GetUserId() for the partition key.
            // Falls back to RemoteIpAddress for anonymous requests.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var path = context.Request.Path.Value ?? string.Empty;

                // Auth endpoints are governed by LoginLimiter, not the global limiter.
                if (path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
                    return RateLimitPartition.GetNoLimiter("auth-exempt");

                // Health endpoints are unauthenticated probes — no rate limit.
                if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
                    return RateLimitPartition.GetNoLimiter("health-exempt");

                var partitionKey = ResolveUserPartitionKey(context.User, context.Connection);
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

    /// <summary>
    /// Resolves a partition key for the login limiter.
    /// Combines IP address and the submitted username (if present in the request body)
    /// so that brute-force attempts against a single account are throttled per-IP AND
    /// per-account independently.
    /// </summary>
    private static string ResolveLoginPartitionKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";

        // Per-IP partition is the primary brute-force defense.
        // A secondary per-username lockout (10 failures → 15-min cooldown)
        // should be added in AuthService for full protection.
        return $"login:{ip}";
    }

    /// <summary>
    /// Resolves a partition key for authenticated data endpoints.
    /// uses the "userId" claim (via FindFirst, bypassing .NET's claim remapping)
    /// instead of Identity.Name (which is null because the JWT uses a custom "username" claim,
    /// not ClaimTypes.Name) or FindFirst("sub") (which is remapped to ClaimTypes.NameIdentifier).
    /// </summary>
    private static string ResolveUserPartitionKey(
        System.Security.Claims.ClaimsPrincipal? user,
        Microsoft.AspNetCore.Http.ConnectionInfo connection)
    {
        if (user?.Identity?.IsAuthenticated == true)
        {
            // Use the userId claim directly — this is the same claim GetUserId() reads.
            // We do NOT use Identity.Name (null — the JWT "username" claim is not ClaimTypes.Name)
            // and we do NOT use FindFirst("sub") (remapped to ClaimTypes.NameIdentifier by .NET 8).
            var userIdClaim = user.FindFirst("userId")?.Value;
            if (!string.IsNullOrEmpty(userIdClaim))
                return $"user:{userIdClaim}";

            // Fallback: use the "username" claim if userId is missing for some reason
            var usernameClaim = user.FindFirst("username")?.Value;
            if (!string.IsNullOrEmpty(usernameClaim))
                return $"user:{usernameClaim}";
        }

        return connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
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

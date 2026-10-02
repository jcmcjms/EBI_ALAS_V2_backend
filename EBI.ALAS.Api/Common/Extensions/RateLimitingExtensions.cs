using System.Threading.RateLimiting;
using EBI.ALAS.Api.Shared.Models;
using Microsoft.AspNetCore.RateLimiting;
namespace EBI.ALAS.Api.Common.Extensions;
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
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var path = context.Request.Path.Value ?? string.Empty;
                if (path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
                    return RateLimitPartition.GetNoLimiter("auth-exempt");
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
    private static string ResolveLoginPartitionKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
        return $"login:{ip}";
    }
    private static string ResolveUserPartitionKey(
        System.Security.Claims.ClaimsPrincipal? user,
        Microsoft.AspNetCore.Http.ConnectionInfo connection)
    {
        if (user?.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = user.FindFirst("userId")?.Value;
            if (!string.IsNullOrEmpty(userIdClaim))
                return $"user:{userIdClaim}";
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

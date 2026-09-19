using System.Text.Json;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Common.Middleware;

/// <summary>
/// CSRF validation middleware. Validates X-XSRF-TOKEN header against the JWT claim.
/// Uses primary constructor for dependency injection.
/// </summary>
public sealed class CsrfValidationMiddleware(
    RequestDelegate next,
    ILogger<CsrfValidationMiddleware> logger)
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };
    public const string XsrfHeaderName = "X-XSRF-TOKEN";
    private const string RefreshEndpointPath = "/api/auth/refresh";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

        if (SafeMethods.Contains(request.Method))
        {
            await next(context);
            return;
        }

        if (request.Path.StartsWithSegments(RefreshEndpointPath, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var xsrfClaim = user.FindFirst(JwtTokenService.XsrfTokenClaim)?.Value;

        if (string.IsNullOrEmpty(xsrfClaim))
        {
            logger.LogWarning("CSRF check failed: missing claim on {Path}", request.Path);
            await WriteForbiddenAsync(context, "CSRF token missing from access token.");
            return;
        }

        if (!request.Headers.TryGetValue(XsrfHeaderName, out var headerValues) || headerValues.Count == 0 || string.IsNullOrEmpty(headerValues[0]))
        {
            logger.LogWarning("CSRF check failed: missing header on {Method} {Path}", request.Method, request.Path);
            await WriteForbiddenAsync(context, $"Missing {XsrfHeaderName} header.");
            return;
        }

        var headerToken = headerValues[0]!;
        if (!CryptographicOperationsFixedTimeEquals(xsrfClaim, headerToken))
        {
            logger.LogWarning("CSRF check failed: token mismatch on {Method} {Path}", request.Method, request.Path);
            await WriteForbiddenAsync(context, "CSRF token does not match access token.");
            return;
        }

        await next(context);
    }

    private static bool CryptographicOperationsFixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static async Task WriteForbiddenAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = ApiResponse.ErrorResponse(message);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await context.Response.WriteAsync(json);
    }
}

public static class CsrfValidationMiddlewareExtensions
{
    public static IApplicationBuilder UseCsrfValidation(this IApplicationBuilder builder)
        => builder.UseMiddleware<CsrfValidationMiddleware>();
}

using System.Text.Json;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// CSRF protection middleware implementing the double-submit cookie pattern.
///
/// <para>
/// On login/refresh the server sets an <c>XSRF-TOKEN</c> cookie whose value
/// is mirrored as an <c>XsrfToken</c> claim inside the JWT. The frontend must
/// read the cookie and echo it in the <c>X-XSRF-TOKEN</c> header on every
/// state-changing request (POST/PUT/PATCH/DELETE).
/// </para>
///
/// <para>
/// This middleware:
/// </para>
/// <list type="bullet">
///   <item>Skips safe HTTP methods (GET/HEAD/OPTIONS).</item>
///   <item>Skips <c>/api/auth/refresh</c> (cookie-authenticated, no header expected).</item>
///   <item>For other authenticated mutations: compares the
///         <c>X-XSRF-TOKEN</c> header to the <c>XsrfToken</c> claim on the bearer
///         token. Rejects mismatches with <c>403 Forbidden</c>.</item>
/// </list>
///
/// <para>
/// Must be registered AFTER <c>UseAuthentication</c> + <c>UseAuthorization</c>
/// so the bearer token (and therefore the XSRF claim) is already attached to
/// <see cref="HttpContext.User"/>.
/// </para>
/// </summary>
public sealed class CsrfValidationMiddleware
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "OPTIONS"
    };

    /// <summary>
    /// Header used by the SPA to echo the CSRF token.
    /// </summary>
    public const string XsrfHeaderName = "X-XSRF-TOKEN";

    /// <summary>
    /// Path the refresh endpoint lives at. Exempted because it is
    /// cookie-authenticated and is invoked via the SPA's session manager
    /// before any user-driven mutation has occurred.
    /// </summary>
    private const string RefreshEndpointPath = "/api/auth/refresh";

    private readonly RequestDelegate _next;
    private readonly ILogger<CsrfValidationMiddleware> _logger;

    // Reusable JsonSerializerOptions cached for the hot path.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Build the CSRF middleware.
    /// </summary>
    public CsrfValidationMiddleware(RequestDelegate next, ILogger<CsrfValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Inspect the incoming HTTP context and either invoke the next
    /// middleware or short-circuit with a 403 response.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

        // ─── Bypass safe methods (CSRF only relevant for state changes) ──
        if (SafeMethods.Contains(request.Method))
        {
            await _next(context);
            return;
        }

        // ─── Bypass refresh endpoint (cookie-authenticated, no header) ──
        if (request.Path.StartsWithSegments(RefreshEndpointPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // ─── Only enforce on authenticated requests ─────────────────────
        // Anonymous mutation attempts are blocked earlier by RequireAuthorization
        // on each endpoint; if we got here without a user, just pass through.
        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        // ─── Compare header ↔ claim ─────────────────────────────────────
        var xsrfClaim = user.FindFirst(JwtTokenService.XsrfTokenClaim)?.Value;

        // Missing claim = token issued before CSRF was wired, or a custom token.
        // Refuse to accept it for state-changing calls.
        if (string.IsNullOrEmpty(xsrfClaim))
        {
            _logger.LogWarning(
                "CSRF check failed: authenticated request to {Path} without {Claim} claim",
                request.Path, JwtTokenService.XsrfTokenClaim);
            await WriteForbiddenAsync(context, "CSRF token missing from access token.");
            return;
        }

        if (!request.Headers.TryGetValue(XsrfHeaderName, out var headerValues)
            || headerValues.Count == 0
            || string.IsNullOrEmpty(headerValues[0]))
        {
            _logger.LogWarning(
                "CSRF check failed: missing {Header} header on {Method} {Path}",
                XsrfHeaderName, request.Method, request.Path);
            await WriteForbiddenAsync(context, $"Missing {XsrfHeaderName} header.");
            return;
        }

        // Constant-time comparison prevents timing attacks.
        var headerToken = headerValues[0]!;
        if (!CryptographicOperationsFixedTimeEquals(xsrfClaim, headerToken))
        {
            _logger.LogWarning(
                "CSRF check failed: header/claim mismatch on {Method} {Path}",
                request.Method, request.Path);
            await WriteForbiddenAsync(context, "CSRF token does not match access token.");
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Compares two strings in time proportional to their length, mitigating
    /// timing side-channels. Uses a managed implementation so we don't depend
    /// on <c>CryptographicOperations.FixedTimeEquals</c> (which requires
    /// ReadOnlySpan&lt;byte&gt;).
    /// </summary>
    private static bool CryptographicOperationsFixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        var diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
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

/// <summary>
/// Convenience extensions for registering <see cref="CsrfValidationMiddleware"/>
/// in the request pipeline.
/// </summary>
public static class CsrfValidationMiddlewareExtensions
{
    /// <summary>
    /// Adds the CSRF validation middleware to the pipeline. Must be called
    /// AFTER <c>UseAuthentication</c> and <c>UseAuthorization</c>.
    /// </summary>
    public static IApplicationBuilder UseCsrfValidation(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CsrfValidationMiddleware>();
    }
}
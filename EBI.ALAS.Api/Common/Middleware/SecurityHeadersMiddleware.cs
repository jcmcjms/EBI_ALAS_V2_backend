using Microsoft.Extensions.Primitives;

namespace EBI.ALAS.Api.Common.Middleware;

/// <summary>
/// Security header middleware. Adds the headers a banking-grade app
/// needs to defend against clickjacking, MIME sniffing, and trivial
/// downgrade attacks. All headers are appended before the response
/// body starts; downstream middleware can still override them when
/// they have a legitimate reason (e.g. CSV export must clear
/// X-Content-Type-Options: nosniff isn't needed for text/csv).
/// </summary>
public class SecurityHeadersMiddleware
{
    private static readonly string[] ExcludedPaths = new[]
    {
        "/swagger",          // Swagger UI is the only iframe we render
        "/health"            // health checks don't need browser headers
    };

    private readonly RequestDelegate _next;
    private readonly bool _isDevelopment;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next;
        _isDevelopment = env.IsDevelopment();
    }

    public Task InvokeAsync(HttpContext context)
    {
        // Apply BEFORE the next middleware so headers go out even if the
        // response body was already partially written. We can't prevent that
        // here, but OnStarting() below fires before the first byte goes on
        // the wire — that's the contract that matters.
        context.Response.OnStarting(() =>
        {
            // Don't bother with browser-targeted headers on API calls
            // routed to non-browser consumers (mobile clients ignore
            // these, probes don't care, but the Swagger UI is a
            // browser surface that does need them).
            var path = context.Request.Path.Value ?? string.Empty;
            if (ExcludedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.CompletedTask;
            }

            var headers = context.Response.Headers;

            // Tell the browser to refuse content-type guessing — stops
            // uploaded-text-being-rendered-as-HTML attacks.
            headers["X-Content-Type-Options"] = "nosniff";

            // Hard clickjacking protection. The API has no legitimate
            // need to be framed by anyone.
            headers["X-Frame-Options"] = "DENY";

            // Legacy XSS auditor toggle — modern browsers ignore this,
            // but it costs nothing and silences scanners.
            headers["X-XSS-Protection"] = "1; mode=block";

            // Restrict referrer leakage to first-party origins only.
            // Same-origin requests are fine; cross-origin only gets the
            // bare origin so no path/query leaks out.
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // HSTS in non-development. 1-year max-age with subdomain
            // coverage; the banking domain always uses TLS once it's
            // been set up properly.
            if (!_isDevelopment)
            {
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            }

            // CSP — strict for the JSON API. The frontend lives on a
            // separate origin so it never needs to embed this API's
            // responses in a page (frame-ancestors already blocks
            // that). default-src 'none' means "no resource of any kind
            // should be loaded", which matches what a JSON-only API
            // actually serves.
            headers["Content-Security-Policy"] =
                "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";

            // Permissions-Policy: turn off every browser feature the
            // API consumer doesn't need. This is defence-in-depth —
            // the API has no UI.
            headers["Permissions-Policy"] =
                "accelerometer=(), geolocation=(), gyroscope=(), " +
                "magnetometer=(), microphone=(), payment=(), camera=(), " +
                "usb=(), interest-cohort=()";

            return Task.CompletedTask;
        });

        return _next(context);
    }
}

/// <summary>
/// Convenience extension so the middleware can be added in Program.cs
/// with a single line.
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this WebApplication app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
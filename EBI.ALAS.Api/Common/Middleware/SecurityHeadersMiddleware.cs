namespace EBI.ALAS.Api.Common.Middleware;

/// <summary>
/// Security header middleware. Adds banking-grade headers to defend against
/// clickjacking, MIME sniffing, and downgrade attacks.
/// Uses primary constructor for dependency injection.
/// </summary>
public sealed class SecurityHeadersMiddleware(
    RequestDelegate next,
    IWebHostEnvironment environment)
{
    private static readonly string[] ExcludedPaths =
    [
        "/scalar",
        "/openapi",
        "/health"
    ];

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (ExcludedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                return Task.CompletedTask;

            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["X-XSS-Protection"] = "1; mode=block";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            if (!environment.IsDevelopment())
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

            headers["Content-Security-Policy"] =
                "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";

            headers["Permissions-Policy"] =
                "accelerometer=(), geolocation=(), gyroscope=(), " +
                "magnetometer=(), microphone=(), payment=(), camera=(), " +
                "usb=(), interest-cohort=()";

            return Task.CompletedTask;
        });

        return next(context);
    }
}

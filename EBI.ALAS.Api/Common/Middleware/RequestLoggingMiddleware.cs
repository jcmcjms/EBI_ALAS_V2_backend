using System.Diagnostics;

namespace EBI.ALAS.Api.Common.Middleware;

/// <summary>
/// Structured request/response logging middleware for observability.
/// Logs HTTP method, path, status code, and elapsed time for every request.
/// Correlation ID is already set by CorrelationIdMiddleware (runs before this).
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    // Paths to exclude from verbose logging (health checks, swagger, etc.)
    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/swagger",
        "/favicon.ico"
    };

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip verbose logging for excluded paths to reduce noise
        if (ExcludedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        var method = context.Request.Method;
        var userAgent = context.Request.Headers.UserAgent.ToString();

        _logger.LogInformation(
            "HTTP {Method} {Path} started | UserAgent={UserAgent}",
            method, path, userAgent);

        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            var statusCode = context.Response.StatusCode;

            if (statusCode >= 500)
            {
                _logger.LogError(
                    "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                    method, path, statusCode, sw.ElapsedMilliseconds);
            }
            else if (statusCode >= 400)
            {
                _logger.LogWarning(
                    "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                    method, path, statusCode, sw.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogInformation(
                    "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                    method, path, statusCode, sw.ElapsedMilliseconds);
            }
        }
    }
}

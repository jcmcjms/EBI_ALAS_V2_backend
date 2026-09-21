using System.Diagnostics;

namespace EBI.ALAS.Api.Common.Middleware;

/// <summary>
/// Structured request/response logging middleware for observability.
/// Logs HTTP method, path, status code, and elapsed time for every request.
/// Uses primary constructor for dependency injection.
/// </summary>
public sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/scalar",
        "/openapi",
        "/favicon.ico"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (ExcludedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var method = context.Request.Method;
        var userAgent = context.Request.Headers.UserAgent.ToString();

        logger.LogInformation(
            "HTTP {Method} {Path} started | UserAgent={UserAgent}",
            method, path, userAgent);

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            var statusCode = context.Response.StatusCode;

            if (statusCode >= 500)
            {
                logger.LogError(
                    "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                    method, path, statusCode, stopwatch.ElapsedMilliseconds);
            }
            else if (statusCode >= 400)
            {
                logger.LogWarning(
                    "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                    method, path, statusCode, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                logger.LogInformation(
                    "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                    method, path, statusCode, stopwatch.ElapsedMilliseconds);
            }
        }
    }
}

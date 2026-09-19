namespace EBI.ALAS.Api.Common.Middleware;

/// <summary>
/// Reads an incoming X-Correlation-Id (or X-Request-Id) header and either
/// reuses the client-supplied value or generates a fresh one.
/// Uses primary constructor for dependency injection.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string RequestIdHeader = "X-Request-Id";

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        var correlationId = ExtractHeader(context, CorrelationHeader)
            ?? ExtractHeader(context, RequestIdHeader)
            ?? Guid.NewGuid().ToString("N");

        context.Items[HttpContextItemsKeys.CorrelationId] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["RequestPath"] = context.Request.Path.Value ?? string.Empty,
            ["RequestMethod"] = context.Request.Method
        }))
        {
            context.Response.OnStarting(() =>
            {
                if (!context.Response.Headers.ContainsKey(CorrelationHeader))
                    context.Response.Headers[CorrelationHeader] = correlationId;

                return Task.CompletedTask;
            });

            await next(context);
        }
    }

    private static string? ExtractHeader(HttpContext context, string headerName)
    {
        if (context.Request.Headers.TryGetValue(headerName, out var values))
        {
            var raw = values.ToString();
            if (!string.IsNullOrWhiteSpace(raw) && raw.Length <= 128)
                return raw.Trim();
        }
        return null;
    }
}

/// <summary>
/// Keys for HttpContext.Items. Centralized to prevent typos.
/// </summary>
public static class HttpContextItemsKeys
{
    public const string CorrelationId = "CorrelationId";
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this WebApplication app)
        => app.UseMiddleware<CorrelationIdMiddleware>();
}

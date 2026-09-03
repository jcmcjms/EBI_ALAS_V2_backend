using Microsoft.AspNetCore.Http;

namespace EBI.ALAS.Api.Common.Middleware;

/// <summary>
/// Reads an incoming <c>X-Correlation-Id</c> (or <c>X-Request-Id</c>)
/// header and either reuses the client-supplied value or generates a
/// fresh one. The id is:
///   * stored on <see cref="HttpContext.Items"/> under <see cref="HttpContextItemsKeys.CorrelationId"/>
///     so downstream code (services, repositories) can attach it to
///     their own structured logs / EF interceptors,
///   * echoed back on the response as <c>X-Correlation-Id</c> so a
///     caller reporting an issue can paste the value into a support
///     ticket and we can grep logs in one go,
///   * pushed into the <see cref="ILogger"/> scope so every log line
///     emitted while the request is in flight carries the id without
///     each call site having to remember.
///
/// Standard <c>X-Request-Id</c> support is also wired up because some
/// load balancers (nginx, Cloudflare) rewrite that header rather than
/// the <c>X-Correlation-Id</c> one — whichever is present wins.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string RequestIdHeader = "X-Request-Id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        // Prefer a caller-supplied correlation id; fall back to the
        // generic X-Request-Id header; finally generate one so the
        // value is never empty.
        var correlationId = ExtractHeader(context, CorrelationHeader)
            ?? ExtractHeader(context, RequestIdHeader)
            ?? Guid.NewGuid().ToString("N");

        context.Items[HttpContextItemsKeys.CorrelationId] = correlationId;

        // Push the id into the log scope so every framework-emitted
        // log line within the request carries {CorrelationId}. The
        // logger passed here is the typed middleware logger; child
        // scopes inherit it.
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["RequestPath"] = context.Request.Path.Value ?? string.Empty,
            ["RequestMethod"] = context.Request.Method
        }))
        {
            // Echo back so the client has it for support reference.
            // OnStarting guarantees the header goes out before the
            // first byte of the body — the client sees it even if the
            // handler later throws.
            context.Response.OnStarting(() =>
            {
                if (!context.Response.Headers.ContainsKey(CorrelationHeader))
                {
                    context.Response.Headers[CorrelationHeader] = correlationId;
                }
                return Task.CompletedTask;
            });

            await _next(context);
        }
    }

    private static string? ExtractHeader(HttpContext context, string headerName)
    {
        if (context.Request.Headers.TryGetValue(headerName, out var values))
        {
            var raw = values.ToString();
            // Defend against pathological inputs: a 10MB header would
            // pin the id in every log line and bury the real signal.
            // 128 chars is more than enough for any sane GUID/ULID/UUID.
            if (!string.IsNullOrWhiteSpace(raw) && raw.Length <= 128)
            {
                return raw.Trim();
            }
        }
        return null;
    }
}

/// <summary>
/// Keys we stash on <see cref="HttpContext.Items"/>. Centralised so a
/// downstream consumer doesn't typo the magic string and silently
/// break correlation in production.
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
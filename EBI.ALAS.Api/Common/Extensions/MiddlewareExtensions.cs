using EBI.ALAS.Api.Common.Middleware;
using EBI.ALAS.Api.Features.Account;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.AuditLogs;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Branches;
using EBI.ALAS.Api.Features.Dashboard;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Features.Presence;
using EBI.ALAS.Api.Features.RoleManagement;
using EBI.ALAS.Api.Features.Users;
using EBI.ALAS.Api.Features.WebLoans;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EBI.ALAS.Api.Common.Extensions;

/// <summary>
/// Middleware pipeline configuration.
/// Extracted from Program.cs to follow Single Responsibility Principle.
/// </summary>
public static class MiddlewareExtensions
{
    /// <summary>
    /// Configures the complete middleware pipeline in the correct order.
    /// Order matters — each middleware can short-circuit the pipeline.
    /// </summary>
    public static WebApplication ConfigureMiddlewarePipeline(this WebApplication app)
    {
        // Development-only middleware
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        if (!app.Environment.IsDevelopment())
            app.UseHttpsRedirection();

        // MUST be before UseCors — works on the wire, not on the framework response object
        app.UseResponseCompression();

        // FIRST — every later log line gets the correlation scope
        app.UseMiddleware<CorrelationIdMiddleware>();

        // Structured request/response logging
        app.UseMiddleware<RequestLoggingMiddleware>();

        app.UseCors("AllowFrontend");
        app.UseMiddleware<GlobalExceptionHandler>();
        app.UseMiddleware<SecurityHeadersMiddleware>();

        // IP allowlisting for admin endpoints
        app.UseMiddleware<IpAllowlistMiddleware>();

        // Must be before rate limiter to catch all requests
        app.UseIdempotency();

        // Response caching for read-heavy endpoints
        app.UseOutputCache();
        app.UseRateLimiter();

        app.UseAuthentication();

        // CSRF protection — AFTER auth (needs JWT claims), BEFORE authorization
        app.UseMiddleware<CsrfValidationMiddleware>();

        app.UseAuthorization();

        return app;
    }

    /// <summary>
    /// Maps all endpoints (health checks, hubs, and feature endpoints).
    /// </summary>
    public static WebApplication MapEndpoints(this WebApplication app)
    {
        app.MapHealthChecks();
        app.MapHubs();
        app.MapFeatureEndpoints();
        return app;
    }

    private static void MapHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json; charset=utf-8";
                var result = new
                {
                    status = report.Status.ToString(),
                    totalDuration = report.TotalDuration.TotalMilliseconds,
                    checks = report.Entries.Select(e => new
                    {
                        name = e.Key,
                        status = e.Value.Status.ToString(),
                        duration = e.Value.Duration.TotalMilliseconds,
                        description = e.Value.Description,
                        exception = e.Value.Exception?.Message
                    })
                };
                await context.Response.WriteAsJsonAsync(result);
            }
        });
    }

    private static void MapHubs(this WebApplication app)
    {
        app.MapHub<NotificationHub>("/hubs/notifications");
    }

    private static void MapFeatureEndpoints(this WebApplication app)
    {
        app.MapAuthEndpoints();
        app.MapUserEndpoints();
        app.MapRoleEndpoints();
        app.MapBranchEndpoints();
        app.MapLoanEndpoints();
        app.MapWorkflowConfigurationEndpoints();
        app.MapChecklistDocumentEndpoints();
        app.MapLoanDeviationEndpoints();
        app.MapDocumentRemarkEndpoints();
        app.MapDashboardEndpoints();
        app.MapAuditLogEndpoints();
        app.MapAccountEndpoints();
        app.MapWebLoanEndpoints();
        app.MapLoanProductEndpoints();
        app.MapNotificationEndpoints();
        app.MapApprovalMatrixEndpoints();
        app.MapPresenceEndpoints();
    }
}

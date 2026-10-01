using EBI.ALAS.Api.Common.Middleware;
using EBI.ALAS.Api.Features.Account;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.AuditLogs;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Branches;
using EBI.ALAS.Api.Features.Dashboard;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.Loans.Endpoints;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Features.Presence;
using EBI.ALAS.Api.Features.RoleManagement;
using EBI.ALAS.Api.Features.Users;
using EBI.ALAS.Api.Features.WebLoans;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
namespace EBI.ALAS.Api.Common.Extensions;
public static class MiddlewareExtensions
{
    public static WebApplication ConfigureMiddlewarePipeline(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference(options =>
            {
                options
                    .WithTitle("EBI.ALAS.V2 API")
                    .WithTheme(ScalarTheme.Kepler)
                    .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
                    .AddDocument("v1", "EBI.ALAS.V2 API", "/openapi/v1.json");
            });
        }
        if (!app.Environment.IsDevelopment())
            app.UseHttpsRedirection();
        app.UseResponseCompression();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseCors("AllowFrontend");
        app.UseMiddleware<GlobalExceptionHandler>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                             | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
            KnownIPNetworks = { },
            KnownProxies = { }
        });
        app.UseMiddleware<IpAllowlistMiddleware>();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseIdempotency();
        app.UseMiddleware<CsrfValidationMiddleware>();
        app.UseAuthorization();
        app.UseOutputCache();
        return app;
    }
    public static WebApplication MapEndpoints(this WebApplication app)
    {
        app.MapHealthChecks();
        app.MapHubs();
        app.MapFeatureEndpoints();
        return app;
    }
    private static void MapHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = async (context, _) =>
            {
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(new { status = "Healthy" });
            }
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
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
                        description = e.Value.Description
                    })
                };
                await context.Response.WriteAsJsonAsync(result);
            }
        }).RequireAuthorization();
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
        app.MapWorkflowQueueEndpoints();
        app.MapWorkflowConfigurationEndpoints();
        app.MapSignatureChainEndpoints();
        app.MapChecklistDocumentEndpoints();
        app.MapLoanDeviationEndpoints();
        app.MapDocumentRemarkEndpoints();
        app.MapDashboardEndpoints();
        app.MapLoanGroupEndpoints();
        app.MapAuditLogEndpoints();
        app.MapAccountEndpoints();
        app.MapWebLoanEndpoints();
        app.MapLoanProductEndpoints();
        app.MapNotificationEndpoints();
        app.MapApprovalMatrixEndpoints();
        app.MapPresenceEndpoints();
    }
}

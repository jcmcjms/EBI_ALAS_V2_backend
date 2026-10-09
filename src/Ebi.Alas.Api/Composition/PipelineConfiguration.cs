using Ebi.Alas.Api.Features.Approvals;
using Ebi.Alas.Api.Features.AuditLogs;
using Ebi.Alas.Api.Features.Auth.Logout;
using Ebi.Alas.Api.Features.Auth.ChangePassword;
using Ebi.Alas.Api.Features.Auth.Login;
using Ebi.Alas.Api.Features.Auth.Refresh;
using Ebi.Alas.Api.Features.Branches;
using Ebi.Alas.Api.Features.Dashboard;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanProducts;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Features.Presence;
using Ebi.Alas.Api.Features.Roles;
using Ebi.Alas.Api.Features.Users;
using Ebi.Alas.Api.Features.WebLoans;
using Ebi.Alas.Api.Features.Workflow;
using Scalar.AspNetCore;

namespace Ebi.Alas.Api.Composition;

public static class PipelineConfiguration
{
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAuthSecurity();
        app.MapOpenApi();
        app.MapScalarApiReference("scalar");
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("ready")
        }).AllowAnonymous();

        app.MapLogin();
        app.MapRefresh();
        app.MapLogout();
        app.MapChangePassword();
        app.MapUsers();
        app.MapRoles();
        app.MapBranches();
        app.MapLoans();
        app.MapLoanWorkflow();
        app.MapWorkflow();
        app.MapDashboardEndpoints();
        app.MapNotificationEndpoints();
        app.MapAuditLogEndpoints();
        app.MapWebLoans();
        app.MapPresenceEndpoints();
        app.MapLoanProductEndpoints();
        app.MapSlaPolicyEndpoints();
        Features.Notifications.NotificationHubModule.MapNotificationHub(app);

        return app;
    }
}

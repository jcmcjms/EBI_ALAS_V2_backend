using System.Security.Claims;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.AuditLogs;
using EBI.ALAS.Api.Features.SystemSettings;
using Microsoft.Extensions.Options;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Workflow feature flags. Bound from appsettings "Workflow" section (or env
/// var Workflow__RequireRecommendation=false in containers). IOptionsMonitor
/// means the file is watched: flipping the value takes effect on the next
/// request WITHOUT a redeploy or restart.
/// </summary>
public sealed class WorkflowOptions
{
    public const string SectionName = "Workflow";

    /// <summary>
    /// true  = Draft → ForRecommendation → ForChecking → ForApproval (classic).
    /// false = Draft → ForChecking → ForApproval (recommender step skipped).
    /// Loans ALREADY in ForRecommendation remain actionable either way.
    /// </summary>
    public bool RequireRecommendation { get; set; } = true;
}

public interface IWorkflowConfiguration
{
    bool RequireRecommendation { get; }

    /// <summary>Status a brand-new submission lands in.</summary>
    string InitialStatus { get; }

    /// <summary>Ordered review stages after submission (for UI steppers).</summary>
    IReadOnlyList<string> Stages { get; }

    /// <summary>"Database" when an admin override exists, else "AppConfig".</summary>
    string Source { get; }

    DateTime? UpdatedAt { get; }
    string? UpdatedByName { get; }

    /// <summary>Called by writer + refresher to propagate DB overrides.</summary>
    void Apply(SettingSnapshot snapshot);
}

public sealed class WorkflowConfiguration : IWorkflowConfiguration
{
    private readonly IOptionsMonitor<WorkflowOptions> _options;
    private SettingSnapshot _override = SettingSnapshot.Empty;

    public WorkflowConfiguration(IOptionsMonitor<WorkflowOptions> options) => _options = options;

    public void Apply(SettingSnapshot snapshot) => _override = snapshot;

    public bool RequireRecommendation =>
        _override.Value ?? _options.CurrentValue.RequireRecommendation;

    public string InitialStatus =>
        RequireRecommendation ? "ForRecommendation" : "ForChecking";

    public IReadOnlyList<string> Stages =>
        RequireRecommendation
            ? ["ForRecommendation", "ForChecking", "ForApproval"]
            : ["ForChecking", "ForApproval"];

    public string Source => _override.Value is null ? "AppConfig" : "Database";
    public DateTime? UpdatedAt => _override.UpdatedAt;
    public string? UpdatedByName => _override.UpdatedByName;
}

/// <summary>
/// Propagates DB overrides written by OTHER instances within 30s.
/// Same pattern as LoanProductSyncHostedService.
/// </summary>
public sealed class WorkflowSettingsRefreshHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IWorkflowConfiguration _config;
    private readonly ILogger<WorkflowSettingsRefreshHostedService> _logger;

    public WorkflowSettingsRefreshHostedService(
        IServiceScopeFactory scopes,
        IWorkflowConfiguration config,
        ILogger<WorkflowSettingsRefreshHostedService> logger)
    {
        _scopes = scopes;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<ISystemSettingsStore>();
                var snapshot = await store.GetSnapshotAsync(
                    SystemSettingKeys.RequireRecommendation, bypassCache: true, ct);
                _config.Apply(snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Workflow settings refresh failed; retrying next cycle.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

public static class WorkflowConfigurationEndpoints
{
    /// <summary>
    /// GET /api/workflow/configuration — the single source of truth the UI
    /// reads so it never hardcodes the pipeline shape.
    /// </summary>
    public static void MapWorkflowConfigurationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/workflow")
            .WithTags("Workflow")
            .RequireAuthorization();

        // GET — readable by any authenticated officer (the UI renders the pipeline).
        group.MapGet("/configuration", (IWorkflowConfiguration cfg) =>
            Results.Ok(ApiResponse<WorkflowConfigurationResponse>.SuccessResponse(Map(cfg))))
        .WithName("GetWorkflowConfiguration")
        .Produces<ApiResponse<WorkflowConfigurationResponse>>(200)
        .RequireAuthorization();

        // PUT — Admin-only flip. Audited, immediately effective on this instance,
        // ≤30s on siblings via the refresh host.
        group.MapPut("/configuration", async (
            UpdateWorkflowConfigurationRequest request,
            ISystemSettingsStore store,
            IWorkflowConfiguration config,
            IAuditLogService auditLogService,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = int.Parse(user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!);
            var firstName = user.FindFirstValue("firstName") ?? "";
            var lastName = user.FindFirstValue("lastName") ?? "";

            var snapshot = await store.SetBoolAsync(
                SystemSettingKeys.RequireRecommendation, request.RequireRecommendation, userId, ct);

            config.Apply(snapshot);   // no wait for the refresh cycle locally

            await auditLogService.LogAsync(
                userId,
                $"{firstName} {lastName}",
                "Update",
                "SystemSetting",
                SystemSettingKeys.RequireRecommendation,
                request.RequireRecommendation.ToString(),
                $"Workflow recommendation step {(request.RequireRecommendation ? "enabled" : "disabled")}");

            return Results.Ok(ApiResponse<WorkflowConfigurationResponse>.SuccessResponse(
                Map(config), "Workflow configuration updated."));
        })
        .WithName("UpdateWorkflowConfiguration")
        .Produces<ApiResponse<WorkflowConfigurationResponse>>(200)
        .RequireAuthorization("CanManageWorkflow");
    }

    private static WorkflowConfigurationResponse Map(IWorkflowConfiguration cfg) => new(
        cfg.RequireRecommendation,
        cfg.InitialStatus,
        cfg.Stages,
        cfg.Source,
        cfg.UpdatedAt,
        cfg.UpdatedByName);
}

public sealed record WorkflowConfigurationResponse(
    bool RequireRecommendation,
    string InitialStatus,
    IReadOnlyList<string> Stages,
    string Source,
    DateTime? UpdatedAt,
    string? UpdatedByName);

public sealed record UpdateWorkflowConfigurationRequest(bool RequireRecommendation);

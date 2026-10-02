using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Shared.Models;
using EBI.ALAS.Api.Shared.Time;
using EBI.ALAS.Api.Features.AuditLogs;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Features.SystemSettings;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace EBI.ALAS.Api.Features.Loans;
public sealed class WorkflowOptions
{
    public const string SectionName = "Workflow";
    public bool RequireRecommendation { get; set; } = true;
    public decimal MinimumNthp { get; set; } = 5_800m;
}
public sealed class QueueOptions
{
    public const string SectionName = "Queue";
    public int LeaseTtlMinutes { get; set; } = 30;
}
public interface IWorkflowConfiguration
{
    bool RequireRecommendation { get; }
    decimal MinimumNthp { get; }
    string InitialStatus { get; }
    IReadOnlyList<string> Stages { get; }
    string Source { get; }
    DateTime? UpdatedAt { get; }
    string? UpdatedByName { get; }
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
    public decimal MinimumNthp =>
        _options.CurrentValue.MinimumNthp;
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
    public static void MapWorkflowConfigurationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/workflow")
            .WithTags("Workflow")
            .RequireAuthorization();
        group.MapGet("/configuration", (IWorkflowConfiguration cfg) =>
            Results.Ok(ApiResponse<WorkflowConfigurationResponse>.SuccessResponse(Map(cfg))))
        .WithName("GetWorkflowConfiguration")
        .Produces<ApiResponse<WorkflowConfigurationResponse>>(200)
        .RequireAuthorization();
        group.MapPut("/configuration", async (
            UpdateWorkflowConfigurationRequest request,
            ISystemSettingsStore store,
            IWorkflowConfiguration config,
            IAuditLogService auditLogService,
            IRealtimeNotificationService realtimeService,
            AppDbContext db,
            ITimeProvider timeProvider,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = int.Parse(user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!);
            var firstName = user.FindFirstValue("firstName") ?? "";
            var lastName = user.FindFirstValue("lastName") ?? "";
            var snapshot = await store.SetBoolAsync(
                SystemSettingKeys.RequireRecommendation, request.RequireRecommendation, userId, ct);
            config.Apply(snapshot);
            await auditLogService.LogAsync(
                userId,
                $"{firstName} {lastName}",
                "Update",
                "SystemSetting",
                SystemSettingKeys.RequireRecommendation,
                request.RequireRecommendation.ToString(),
                $"Workflow recommendation step {(request.RequireRecommendation ? "enabled" : "disabled")}");
            var workflowRoles = new[] { Roles.Encoder, Roles.Recommender, Roles.Evaluator, Roles.Approver, Roles.Admin };
            var activeUsers = await db.Users
                .Where(u => u.IsActive && workflowRoles.Contains(u.Role))
                .ToListAsync(ct);
            var title = request.RequireRecommendation
                ? "System Update: Recommendation Step Enabled"
                : "System Update: Recommendation Step Disabled";
            var description = request.RequireRecommendation
                ? "The Branch Head recommendation step is now required for new loan applications."
                : "The Branch Head recommendation step is now skipped. New applications will go straight to evaluation.";
            var notifications = activeUsers.Select(u => new Notification
            {
                UserId = u.Id,
                Title = title,
                Description = description,
                Link = "/admin/workflow",
                CreatedAt = timeProvider.UtcNow,
                IsRead = false
            }).ToList();
            if (notifications.Count > 0)
            {
                db.Notifications.AddRange(notifications);
                await db.SaveChangesAsync(ct);
            }
            await realtimeService.NotifyAllAsync(title, description, "/admin/workflow");
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

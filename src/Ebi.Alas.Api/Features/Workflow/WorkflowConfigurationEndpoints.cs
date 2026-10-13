using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Features.SystemSettings;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Workflow;

public static class WorkflowConfigurationEndpoints
{
    public static void MapWorkflowConfigurationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workflow")
            .WithTags("Workflow")
            .RequireAuthorization();

        group.MapGet("/configuration", async (
            IWorkflowConfiguration cfg,
            CancellationToken ct) =>
        {
            await cfg.EnsureLoadedAsync(ct);
            return Results.Ok(Map(cfg));
        })
        .WithName("GetWorkflowConfiguration")
        .Produces<WorkflowConfigurationResponse>(200)
        .RequireAuthorization();

        group.MapPut("/configuration", async (
            UpdateWorkflowConfigurationRequest request,
            ISystemSettingsStore store,
            IWorkflowConfiguration config,
            IHubContext<NotificationHub> hubContext,
            AlasDbContext db,
            TimeProvider timeProvider,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub") ?? Guid.Empty.ToString());

            var snapshot = await store.SetBoolAsync(
                SystemSettingKeys.RequireRecommendation, request.RequireRecommendation, userId, ct);

            config.Apply(snapshot);

            var workflowRoles = new[] { UserRole.Encoder, UserRole.Recommender, UserRole.Evaluator, UserRole.Approver, UserRole.Admin };
            var activeUsers = await db.Users
                .Where(u => u.Status == UserStatus.Active && workflowRoles.Contains(u.Role))
                .ToListAsync(ct);

            var title = request.RequireRecommendation
                ? "System Update: Recommendation Step Enabled"
                : "System Update: Recommendation Step Disabled";
            var description = request.RequireRecommendation
                ? "The Branch Head recommendation step is now required for new loan applications."
                : "The Branch Head recommendation step is now skipped. New applications will go straight to evaluation.";

            var notifications = activeUsers.Select(u => Notification.Create(
                u.Id,
                title,
                description,
                NotificationType.System,
                timeProvider.GetUtcNow())).ToList();

            if (notifications.Count > 0)
            {
                db.Notifications.AddRange(notifications);
                await db.SaveChangesAsync(ct);
            }

            foreach (var notification in notifications)
            {
                var payload = NotificationHub.ToReceiveNotification(
                    notification.Title,
                    notification.Body,
                    notification.CreatedAt);

                await hubContext.Clients.Group(NotificationHub.UserGroup(notification.UserId))
                    .SendAsync(NotificationHub.ReceiveNotificationEvent, payload, ct);
            }

            return Results.Ok(Map(config));
        })
        .WithName("UpdateWorkflowConfiguration")
        .Produces<WorkflowConfigurationResponse>(200)
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
using System.Security.Claims;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Features.Notifications;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .RequireAuthorization();

        // GET /api/notifications — most-recent N notifications for the calling user.
        // The SPA's header bell calls this on a 30s poll. No permission policy
        // is required beyond authentication — every authenticated user can read
        // their own notifications.
        group.MapGet("/", async (ClaimsPrincipal principal, INotificationService service) =>
        {
            var userId = principal.GetUserId();
            var notifications = await service.GetUserNotificationsAsync(userId);
            return Results.Ok(ApiResponse<List<NotificationResponse>>.SuccessResponse(notifications));
        })
        .WithName("GetNotifications")
        .Produces<ApiResponse<List<NotificationResponse>>>(200);
    }
}
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

        // GET /api/notifications — server-driven paged inbox.
        // Accepts optional query params for status/type/search filtering
        // and pagination. The SPA's notification page calls this.
        group.MapGet("/", async (
            ClaimsPrincipal principal,
            INotificationService service,
            int? page,
            int? pageSize,
            string? status,
            string? type,
            string? search,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();

            // Sanitize inputs
            var allowedStatus = (status ?? "all").ToLowerInvariant();
            if (allowedStatus is not ("unread" or "read"))
                allowedStatus = "all";

            var query = new InboxQuery(
                Page: Math.Max(1, page ?? 1),
                PageSize: Math.Clamp(pageSize ?? 10, 1, 50), // server-side cap
                Status: allowedStatus,
                Type: type,
                Search: search);

            var inbox = await service.GetInboxAsync(userId, query, ct);
            return Results.Ok(ApiResponse<InboxPage>.SuccessResponse(inbox));
        })
        .WithName("GetNotificationInbox")
        .Produces<ApiResponse<InboxPage>>(200);

        // GET /api/notifications/recent — backward-compatible endpoint for
        // the header bell. Returns the most recent 20 notifications.
        group.MapGet("/recent", async (ClaimsPrincipal principal, INotificationService service) =>
        {
            var userId = principal.GetUserId();
            var notifications = await service.GetUserNotificationsAsync(userId);
            return Results.Ok(ApiResponse<List<NotificationResponse>>.SuccessResponse(notifications));
        })
        .WithName("GetNotifications")
        .Produces<ApiResponse<List<NotificationResponse>>>(200);

        // PUT /api/notifications/read-all — mark all unread as read.
        // Idempotent — second call returns 0 changed.
        group.MapPut("/read-all", async (
            ClaimsPrincipal principal,
            INotificationService service,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var changed = await service.MarkAllReadAsync(userId, ct);
            return Results.Ok(ApiResponse<MarkAllReadResponse>.SuccessResponse(
                new MarkAllReadResponse(changed)));
        })
        .WithName("MarkAllNotificationsRead")
        .Produces<ApiResponse<MarkAllReadResponse>>(200);

        // PUT /api/notifications/{id}/read — mark a single notification as read.
        // /read-all and /{id:int}/read can't collide — the int constraint
        // rejects "read-all". Idempotent — re-reading keeps original ReadAt.
        group.MapPut("/{id:int}/read", async (
            ClaimsPrincipal principal,
            int id,
            INotificationService service,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var found = await service.MarkReadAsync(userId, id, ct);
            return found
                ? Results.Ok(ApiResponse<MarkAllReadResponse>.SuccessResponse(
                    new MarkAllReadResponse(1)))
                : Results.NotFound(ApiResponse<MarkAllReadResponse>.ErrorResponse(
                    "Notification not found."));
        })
        .WithName("MarkNotificationRead")
        .Produces<ApiResponse<MarkAllReadResponse>>(200)
        .Produces<ApiResponse<MarkAllReadResponse>>(404);
    }
}

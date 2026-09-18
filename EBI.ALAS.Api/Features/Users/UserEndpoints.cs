using System.Security.Claims;
using EBI.ALAS.Api.Common.Exceptions;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.AuditLogs;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace EBI.ALAS.Api.Features.Users;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users");

        group.MapGet("/", async ([AsParameters] UserQueryParameters parameters, IUserService userService) =>
        {
            var result = await userService.GetUsersAsync(parameters);
            return Results.Ok(ApiResponse<PagedResult<UserResponse>>.SuccessResponse(result));
        }).WithName("GetUsers").RequireAuthorization("CanViewUsers");

        group.MapGet("/{id:int}", async (int id, IUserService userService) =>
        {
            var user = await userService.GetUserByIdAsync(id);
            return user is null
                ? Results.NotFound(ApiResponse.ErrorResponse("User not found"))
                : Results.Ok(ApiResponse<UserResponse>.SuccessResponse(user));
        }).WithName("GetUserById").RequireAuthorization("CanViewUsers");

        group.MapPost("/", async ([FromBody] CreateUserRequest request, IValidator<CreateUserRequest> validator, IUserService userService, IAuditLogService auditLogService, ClaimsPrincipal principal) =>
        {
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
                return Results.BadRequest(ApiResponse.ErrorResponse("Validation failed", validationResult.Errors.Select(e => e.ErrorMessage).ToList()));

            try
            {
                var user = await userService.CreateUserAsync(request);

                // CUD audit: capture the user-creation event in the global
                // AuditLog table so it shows up alongside loan/workflow events
                // on the Audit Logs page.
                await auditLogService.LogAsync(
                    principal.GetUserId(),
                    $"{principal.GetFirstName()} {principal.GetLastName()}",
                    "Create", "User", user.Id.ToString(), user.Username,
                    $"Created user {user.Username}");

                return Results.Created($"/api/users/{user.Id}", ApiResponse<UserResponse>.SuccessResponse(user, "User created successfully"));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ApiResponse.ErrorResponse(ex.Message));
            }
        }).WithName("CreateUser").RequireAuthorization("CanCreateUsers");

        group.MapPut("/{id:int}", async (int id, [FromBody] UpdateUserRequest request, IValidator<UpdateUserRequest> validator, IUserService userService, IAuditLogService auditLogService, ClaimsPrincipal principal) =>
        {
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
                return Results.BadRequest(ApiResponse.ErrorResponse("Validation failed", validationResult.Errors.Select(e => e.ErrorMessage).ToList()));

            var user = await userService.UpdateUserAsync(id, request);
            if (user is not null)
            {
                await auditLogService.LogAsync(
                    principal.GetUserId(),
                    $"{principal.GetFirstName()} {principal.GetLastName()}",
                    "Update", "User", id.ToString(), user.Username,
                    $"Updated user details for {user.Username}");
            }
            return user is null
                ? Results.NotFound(ApiResponse.ErrorResponse("User not found"))
                : Results.Ok(ApiResponse<UserResponse>.SuccessResponse(user, "User updated successfully"));
        }).WithName("UpdateUser").RequireAuthorization("CanEditUsers");

        group.MapPatch("/{id:int}/status", async (int id, [FromBody] UserStatusRequest request, IUserService userService, IAuditLogService auditLogService, ClaimsPrincipal principal) =>
        {
            // Resolve the user BEFORE the status flip so we can label the
            // audit entry with the username — the username is the
            // EntityLabel the Audit Logs page filters on.
            var user = await userService.GetUserByIdAsync(id);
            var success = await userService.UpdateUserStatusAsync(id, request.IsActive);
            if (success && user is not null)
            {
                await auditLogService.LogAsync(
                    principal.GetUserId(),
                    $"{principal.GetFirstName()} {principal.GetLastName()}",
                    "StatusChange", "User", id.ToString(), user.Username,
                    $"User status changed to {(request.IsActive ? "Active" : "Suspended")}");
            }
            return success
                ? Results.Ok(ApiResponse.SuccessResponse($"User status updated to {(request.IsActive ? "Active" : "Suspended")}"))
                : Results.NotFound(ApiResponse.ErrorResponse("User not found"));
        }).WithName("UpdateUserStatus").RequireAuthorization("CanSuspendUsers");

        group.MapPost("/{id:int}/reset-password", async (
            int id,
            [FromBody] ResetPasswordRequest? request,
            IUserService userService,
            ITempPasswordGenerator generator,
            IAuditLogService auditLogService,
            ClaimsPrincipal principal) =>
        {
            var supplied = request?.NewPassword;
            string newPassword;

            if (string.IsNullOrWhiteSpace(supplied))
            {
                // Server is the policy authority — generate on behalf of the admin.
                newPassword = generator.Generate();
            }
            else
            {
                newPassword = supplied;
            }

            try
            {
                var result = await userService.ResetPasswordAsync(id, newPassword);

                // Compliance trail — NEVER the credential itself.
                await auditLogService.LogAsync(
                    principal.GetUserId(),
                    $"{principal.GetFirstName()} {principal.GetLastName()}",
                    "PasswordReset", "User", id.ToString(), result.Username,
                    "Temporary credential issued; change required at next login.");

                return Results.Ok(ApiResponse<ResetPasswordResponse>.SuccessResponse(result,
                    "Password reset. Display the credential once via the secure handoff dialog."));
            }
            catch (NotFoundException ex)
            {
                return Results.NotFound(ApiResponse.ErrorResponse(ex.Message));
            }
        }).WithName("ResetUserPassword").RequireAuthorization("CanEditUsers");

        group.MapPost("/{id:int}/force-password-reset", async (int id, IUserService userService) =>
        {
            var success = await userService.ForcePasswordResetAsync(id);
            return success
                ? Results.Ok(ApiResponse.SuccessResponse("User will be required to change password on next login"))
                : Results.NotFound(ApiResponse.ErrorResponse("User not found"));
        }).WithName("ForcePasswordReset").RequireAuthorization("CanEditUsers");

        group.MapPost("/{id:int}/revoke-sessions", async (int id, IUserService userService) =>
        {
            try
            {
                var revokedCount = await userService.RevokeAllSessionsAsync(id);
                return Results.Ok(ApiResponse<int>.SuccessResponse(revokedCount, $"Revoked {revokedCount} active session(s)"));
            }
            catch (NotFoundException ex)
            {
                return Results.NotFound(ApiResponse.ErrorResponse(ex.Message));
            }
        }).WithName("RevokeUserSessions").RequireAuthorization("CanSuspendUsers");

        group.MapGet("/{id:int}/audit-log", async (int id, IUserService userService, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20) =>
        {
            var auditLog = await userService.GetAuditLogAsync(id, pageNumber, pageSize);
            return Results.Ok(ApiResponse<List<UserAuditLogResponse>>.SuccessResponse(auditLog));
        }).WithName("GetUserAuditLog").RequireAuthorization("CanViewUsers");

        // ─── Import/Export Endpoints ────────────────────────────────────────

        group.MapGet("/export", async (
            [AsParameters] ExportUsersParameters parameters,
            IUserImportService importService,
            CancellationToken ct) =>
        {
            var bytes = await importService.ExportUsersAsync(parameters, ct);
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"users-export-{DateTime.UtcNow:yyyyMMdd}.xlsx");
        })
        .WithName("ExportUsers")
        .RequireAuthorization("CanViewUsers");

        group.MapGet("/import/template", async (
            IUserImportService importService,
            CancellationToken ct) =>
        {
            var bytes = await importService.GenerateTemplateAsync(ct);
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "user-import-template.xlsx");
        })
        .WithName("GetUserImportTemplate")
        .RequireAuthorization("CanCreateUsers");

        group.MapPost("/import", async (
            IFormFile file,
            IUserImportService importService,
            IAuditLogService auditLogService,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (file == null || file.Length == 0)
                return Results.BadRequest(ApiResponse.ErrorResponse("No file uploaded"));

            if (file.Length > 10 * 1024 * 1024) // 10 MB limit
                return Results.BadRequest(ApiResponse.ErrorResponse("File size exceeds 10 MB limit"));

            if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(ApiResponse.ErrorResponse("Only .xlsx files are supported"));

            using var stream = file.OpenReadStream();
            var result = await importService.ImportUsersAsync(stream, principal.GetUserId(),
                $"{principal.GetFirstName()} {principal.GetLastName()}", ct);

            // Batch audit
            await auditLogService.LogAsync(
                principal.GetUserId(),
                $"{principal.GetFirstName()} {principal.GetLastName()}",
                "Import",
                "UserBatch",
                result.TotalRows.ToString(),
                $"{result.SuccessfulImports} imported",
                $"Imported {result.SuccessfulImports} users ({result.FailedImports} failed)");

            return Results.Ok(ApiResponse<UserImportResult>.SuccessResponse(result,
                $"Imported {result.SuccessfulImports} of {result.TotalRows} users"));
        })
        .WithName("ImportUsers")
        .RequireAuthorization("CanCreateUsers")
        .DisableAntiforgery(); // For multipart/form-data uploads
    }
}

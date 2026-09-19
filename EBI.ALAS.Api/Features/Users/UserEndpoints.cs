using EBI.ALAS.Api.Common.Exceptions;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.AuditLogs;
using Microsoft.AspNetCore.Mvc;

namespace EBI.ALAS.Api.Features.Users;

/// <summary>
/// User management endpoints: CRUD, status, password reset, import/export.
/// Follows Clean Code: small handler functions, early returns, no nested conditionals.
/// </summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users");

        group.MapGet("/", HandleGetUsers)
            .WithName("GetUsers")
            .RequireAuthorization("CanViewUsers");

        group.MapGet("/{id:int}", HandleGetUserById)
            .WithName("GetUserById")
            .RequireAuthorization("CanViewUsers");

        group.MapPost("/", HandleCreateUser)
            .WithName("CreateUser")
            .RequireAuthorization("CanCreateUsers");

        group.MapPut("/{id:int}", HandleUpdateUser)
            .WithName("UpdateUser")
            .RequireAuthorization("CanEditUsers");

        group.MapPatch("/{id:int}/status", HandleUpdateUserStatus)
            .WithName("UpdateUserStatus")
            .RequireAuthorization("CanSuspendUsers");

        group.MapPost("/{id:int}/reset-password", HandleResetPassword)
            .WithName("ResetUserPassword")
            .RequireAuthorization("CanEditUsers");

        group.MapPost("/{id:int}/force-password-reset", HandleForcePasswordReset)
            .WithName("ForcePasswordReset")
            .RequireAuthorization("CanEditUsers");

        group.MapPost("/{id:int}/revoke-sessions", HandleRevokeSessions)
            .WithName("RevokeUserSessions")
            .RequireAuthorization("CanSuspendUsers");

        group.MapGet("/{id:int}/audit-log", HandleGetUserAuditLog)
            .WithName("GetUserAuditLog")
            .RequireAuthorization("CanViewUsers");

        group.MapGet("/export", HandleExportUsers)
            .WithName("ExportUsers")
            .RequireAuthorization("CanViewUsers");

        group.MapGet("/import/template", HandleGetImportTemplate)
            .WithName("GetUserImportTemplate")
            .RequireAuthorization("CanCreateUsers");

        group.MapPost("/import", HandleImportUsers)
            .WithName("ImportUsers")
            .RequireAuthorization("CanCreateUsers")
            .DisableAntiforgery();
    }

    private static async Task<IResult> HandleGetUsers(
        [AsParameters] UserQueryParameters parameters,
        IUserService userService)
    {
        var result = await userService.GetUsersAsync(parameters);
        return Results.Ok(ApiResponse<PagedResult<UserResponse>>.SuccessResponse(result));
    }

    private static async Task<IResult> HandleGetUserById(
        int id,
        IUserService userService)
    {
        var user = await userService.GetUserByIdAsync(id);
        return user is null
            ? Results.NotFound(ApiResponse.ErrorResponse("User not found"))
            : Results.Ok(ApiResponse<UserResponse>.SuccessResponse(user));
    }

    private static async Task<IResult> HandleCreateUser(
        CreateUserRequest request,
        IValidator<CreateUserRequest> validator,
        IUserService userService,
        IAuditLogService auditLogService,
        ClaimsPrincipal principal)
    {
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
            return Results.BadRequest(ApiResponse.ErrorResponse("Validation failed", validationResult.Errors.Select(e => e.ErrorMessage).ToList()));

        try
        {
            var user = await userService.CreateUserAsync(request);

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
    }

    private static async Task<IResult> HandleUpdateUser(
        int id,
        UpdateUserRequest request,
        IValidator<UpdateUserRequest> validator,
        IUserService userService,
        IAuditLogService auditLogService,
        ClaimsPrincipal principal)
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
    }

    private static async Task<IResult> HandleUpdateUserStatus(
        int id,
        UserStatusRequest request,
        IUserService userService,
        IAuditLogService auditLogService,
        ClaimsPrincipal principal)
    {
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
    }

    private static async Task<IResult> HandleResetPassword(
        int id,
        ResetPasswordRequest? request,
        IUserService userService,
        ITempPasswordGenerator generator,
        IAuditLogService auditLogService,
        ClaimsPrincipal principal)
    {
        var supplied = request?.NewPassword;
        var newPassword = string.IsNullOrWhiteSpace(supplied)
            ? generator.Generate()
            : supplied;

        try
        {
            var result = await userService.ResetPasswordAsync(id, newPassword);

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
    }

    private static async Task<IResult> HandleForcePasswordReset(
        int id,
        IUserService userService)
    {
        var success = await userService.ForcePasswordResetAsync(id);
        return success
            ? Results.Ok(ApiResponse.SuccessResponse("User will be required to change password on next login"))
            : Results.NotFound(ApiResponse.ErrorResponse("User not found"));
    }

    private static async Task<IResult> HandleRevokeSessions(
        int id,
        IUserService userService)
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
    }

    private static async Task<IResult> HandleGetUserAuditLog(
        int id,
        IUserService userService,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var auditLog = await userService.GetAuditLogAsync(id, pageNumber, pageSize);
        return Results.Ok(ApiResponse<List<UserAuditLogResponse>>.SuccessResponse(auditLog));
    }

    private static async Task<IResult> HandleExportUsers(
        [AsParameters] ExportUsersParameters parameters,
        IUserImportService importService,
        CancellationToken ct)
    {
        var bytes = await importService.ExportUsersAsync(parameters, ct);
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"users-export-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    private static async Task<IResult> HandleGetImportTemplate(
        IUserImportService importService,
        CancellationToken ct)
    {
        var bytes = await importService.GenerateTemplateAsync(ct);
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "user-import-template.xlsx");
    }

    private static async Task<IResult> HandleImportUsers(
        IFormFile file,
        IUserImportService importService,
        IAuditLogService auditLogService,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest(ApiResponse.ErrorResponse("No file uploaded"));

        if (file.Length > 10 * 1024 * 1024)
            return Results.BadRequest(ApiResponse.ErrorResponse("File size exceeds 10 MB limit"));

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(ApiResponse.ErrorResponse("Only .xlsx files are supported"));

        using var stream = file.OpenReadStream();
        var result = await importService.ImportUsersAsync(stream, principal.GetUserId(),
            $"{principal.GetFirstName()} {principal.GetLastName()}", ct);

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
    }
}

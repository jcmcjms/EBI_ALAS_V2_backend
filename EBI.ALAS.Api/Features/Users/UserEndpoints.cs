using EBI.ALAS.Api.Common.Exceptions;
using EBI.ALAS.Api.Common.Models;
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

        group.MapPost("/", async ([FromBody] CreateUserRequest request, IValidator<CreateUserRequest> validator, IUserService userService) =>
        {
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
                return Results.BadRequest(ApiResponse.ErrorResponse("Validation failed", validationResult.Errors.Select(e => e.ErrorMessage).ToList()));

            try
            {
                var user = await userService.CreateUserAsync(request);
                return Results.Created($"/api/users/{user.Id}", ApiResponse<UserResponse>.SuccessResponse(user, "User created successfully"));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ApiResponse.ErrorResponse(ex.Message));
            }
        }).WithName("CreateUser").RequireAuthorization("CanCreateUsers");

        group.MapPut("/{id:int}", async (int id, [FromBody] UpdateUserRequest request, IValidator<UpdateUserRequest> validator, IUserService userService) =>
        {
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
                return Results.BadRequest(ApiResponse.ErrorResponse("Validation failed", validationResult.Errors.Select(e => e.ErrorMessage).ToList()));

            var user = await userService.UpdateUserAsync(id, request);
            return user is null
                ? Results.NotFound(ApiResponse.ErrorResponse("User not found"))
                : Results.Ok(ApiResponse<UserResponse>.SuccessResponse(user, "User updated successfully"));
        }).WithName("UpdateUser").RequireAuthorization("CanEditUsers");

        group.MapPatch("/{id:int}/status", async (int id, [FromBody] UserStatusRequest request, IUserService userService) =>
        {
            var success = await userService.UpdateUserStatusAsync(id, request.IsActive);
            return success
                ? Results.Ok(ApiResponse.SuccessResponse($"User status updated to {(request.IsActive ? "Active" : "Suspended")}"))
                : Results.NotFound(ApiResponse.ErrorResponse("User not found"));
        }).WithName("UpdateUserStatus").RequireAuthorization("CanSuspendUsers");

        group.MapPost("/{id:int}/reset-password", async (int id, [FromBody] ResetPasswordRequest request, IUserService userService) =>
        {
            try
            {
                var tempPassword = await userService.ResetPasswordAsync(id, request.NewPassword);
                return Results.Ok(ApiResponse<string>.SuccessResponse(tempPassword, "Password reset successfully. User must change password on next login."));
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
    }
}

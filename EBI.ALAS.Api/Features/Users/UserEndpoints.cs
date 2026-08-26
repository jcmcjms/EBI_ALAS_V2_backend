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
    }
}

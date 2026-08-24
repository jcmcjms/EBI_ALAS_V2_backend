using System.Security.Claims;
using Alas.Api.Validation;
using Alas.Application.Admin.Users;
using Alas.Application.Common.Security;
using Alas.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Alas.Api.Endpoints.Admin;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
            .RequireAuthorization(AlasPermissions.UsersManage)
            .WithTags("User Management");

        group.MapGet("/", ListUsersAsync)
            .Produces<PagedResult<UserListItemDto>>();

        group.MapGet("/{id:guid}", GetUserAsync)
            .Produces<UserDetailDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateUserAsync)
            .AddEndpointFilter<ValidationFilter<CreateUserRequest>>()
            .Produces<UserDetailDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPut("/{id:guid}/status", UpdateUserStatusAsync)
            .AddEndpointFilter<ValidationFilter<UpdateUserStatusRequest>>()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/roles", AssignRolesAsync)
            .AddEndpointFilter<ValidationFilter<AssignRolesRequest>>()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListUsersAsync(
        [AsParameters] UserQueryParams queryParams,
        UserService userService,
        CancellationToken cancellationToken)
    {
        var result = await userService.ListAsync(
            queryParams.Page,
            queryParams.PageSize,
            queryParams.Search,
            cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetUserAsync(
        Guid id,
        UserService userService,
        CancellationToken cancellationToken)
    {
        var user = await userService.GetDetailAsync(id, cancellationToken);

        return user is null
            ? Results.NotFound()
            : Results.Ok(user);
    }

    private static async Task<IResult> CreateUserAsync(
        CreateUserRequest request,
        UserService userService,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await userService.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/admin/users/{user!.UserId}", user);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> UpdateUserStatusAsync(
        Guid id,
        UpdateUserStatusRequest request,
        UserService userService,
        CancellationToken cancellationToken)
    {
        var success = await userService.UpdateStatusAsync(
            id, request.IsActive, cancellationToken);

        return success
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static async Task<IResult> AssignRolesAsync(
        Guid id,
        AssignRolesRequest request,
        UserService userService,
        CancellationToken cancellationToken)
    {
        var success = await userService.AssignRolesAsync(
            id, request.Roles, cancellationToken);

        return success
            ? Results.NoContent()
            : Results.NotFound();
    }
}

public sealed record UserQueryParams
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string? Search { get; init; }
}

using Ebi.Alas.Api.Features.Pagination;
using Ebi.Alas.Api.Features.Users.ChangeUserStatus;
using Ebi.Alas.Api.Features.Users.CreateUser;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Users.GetUser;
using Ebi.Alas.Api.Features.Users.ListUsers;
using Ebi.Alas.Api.Features.Users.UpdateUser;
using Microsoft.AspNetCore.Authorization;

namespace Ebi.Alas.Api.Features.Users;

public static class UserEndpoints
{
    public static void MapUsers(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/users").WithTags("Users").RequireAuthorization(new AuthorizeAttribute { Roles = nameof(UserRole.Admin) });

        group.MapPost("/", async (
            CreateUserRequest request,
            CreateUserHandler handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request, cancellationToken);
            return outcome switch
            {
                CreateUserOutcome.Success success => Results.Created($"/api/users/{success.User.Id}", new CreateUserResponse(success.User)),
                CreateUserOutcome.Failure failure => Results.Problem(
                    statusCode: failure.StatusCode,
                    title: failure.StatusCode == StatusCodes.Status409Conflict ? "Conflict" : "Bad Request",
                    detail: failure.Detail),
                _ => Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Server Error",
                    detail: "An unexpected error occurred.")
            };
        });

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            ListUsersHandler handler,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var options = configuration.GetSection("Api").Get<Composition.ApiOptions>()
                ?? new Composition.ApiOptions();
            var result = await handler.HandleAsync(
                new PageRequest { Page = page ?? 1, PageSize = pageSize ?? options.DefaultPageSize },
                options.MaxPageSize,
                options.DefaultPageSize,
                cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/{id:guid}", async (Guid id, GetUserHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, cancellationToken);
            return Results.Ok(result);
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateUserRequest request,
            UpdateUserHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, request, cancellationToken);
            return Results.Ok(result);
        });

        group.MapPost("/{id:guid}/suspend", async (
            Guid id,
            ChangeUserStatusHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, UserStatus.Suspended, cancellationToken);
            return Results.Ok(result);
        });

        group.MapPost("/{id:guid}/activate", async (
            Guid id,
            ChangeUserStatusHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, UserStatus.Active, cancellationToken);
            return Results.Ok(result);
        });
    }
}

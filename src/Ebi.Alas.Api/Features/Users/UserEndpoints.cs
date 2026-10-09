using Ebi.Alas.Api.Features.AuditLogs;
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

        group.MapGet("/import-template", () =>
            Results.File(
                ImportUsers.UserExcel.BuildTemplate(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "user-import-template.xlsx"))
            .RequireRateLimiting("write");

        group.MapPost("/import", async (
            HttpRequest request,
            ImportUsers.ImportUsersHandler handler,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status415UnsupportedMediaType,
                    title: "Unsupported Media Type",
                    detail: "Upload the spreadsheet as multipart/form-data with a 'file' field.");
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: "A non-empty 'file' upload is required.");
            }

            if (file.Length > 10 * 1024 * 1024)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    title: "Payload Too Large",
                    detail: "Import file must be at most 10 MB.");
            }

            await using var stream = file.OpenReadStream();
            var result = await handler.ImportAsync(stream, cancellationToken);
            return Results.Ok(result);
        })
        .RequireRateLimiting("write")
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(11 * 1024 * 1024));

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
            AuditLogs.IAuditLogService audit,
            System.Security.Claims.ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, request, cancellationToken);
            await audit.LogAsync(
                user.GetUserId(),
                user.GetUsername(),
                "Update",
                "User",
                id.ToString(),
                result.UserName,
                $"Updated user {result.UserName}",
                cancellationToken: cancellationToken);
            return Results.Ok(result);
        });

        group.MapPost("/{id:guid}/suspend", async (
            Guid id,
            ChangeUserStatusHandler handler,
            AuditLogs.IAuditLogService audit,
            System.Security.Claims.ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, UserStatus.Suspended, cancellationToken);
            await audit.LogAsync(
                user.GetUserId(),
                user.GetUsername(),
                "StatusChange",
                "User",
                id.ToString(),
                result.UserName,
                $"Suspended user {result.UserName}",
                cancellationToken: cancellationToken);
            return Results.Ok(result);
        });

        group.MapPost("/{id:guid}/activate", async (
            Guid id,
            ChangeUserStatusHandler handler,
            AuditLogs.IAuditLogService audit,
            System.Security.Claims.ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, UserStatus.Active, cancellationToken);
            await audit.LogAsync(
                user.GetUserId(),
                user.GetUsername(),
                "StatusChange",
                "User",
                id.ToString(),
                result.UserName,
                $"Activated user {result.UserName}",
                cancellationToken: cancellationToken);
            return Results.Ok(result);
        });
    }
}

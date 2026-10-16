using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Users.Domain;

namespace Ebi.Alas.Api.Features.Account;

public sealed record UpdateAccountContactRequest(
    string? Email,
    string? Phone,
    string? EmergencyContact,
    string? ESignature);

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/account").WithTags("Account").RequireAuthorization();

        group.MapGet("/me", async (
            ClaimsPrincipal user,
            AccountService service,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var profile = await service.GetProfileAsync(caller.UserId, cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .WithName("GetAccountProfile");

        group.MapPut("/me", async (
            ClaimsPrincipal user,
            UpdateAccountContactRequest request,
            AccountService service,
            CancellationToken cancellationToken) =>
        {
            if (request.ESignature is not null && !ESignaturePolicy.IsValid(request.ESignature))
            {
                throw new ValidationException(
                    "E-signature must be a base64 PNG, JPEG or WEBP data URL under 2 MB.");
            }

            var caller = CallerContext.Require(user);
            var ok = await service.UpdateContactAsync(
                caller.UserId,
                request.Email,
                request.Phone,
                request.EmergencyContact,
                request.ESignature,
                cancellationToken);
            return ok ? Results.NoContent() : Results.NotFound();
        })
        .RequireRateLimiting("write")
        .WithName("UpdateAccountProfile");

        group.MapGet("/me/activity", async (
            ClaimsPrincipal user,
            int? limit,
            DateOnly? from,
            DateOnly? to,
            string? action,
            AccountService service,
            CancellationToken cancellationToken) =>
        {
            if (from is not null && to is not null && from > to)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: "Activity date range start must be on or before end.");
            }

            if (!string.IsNullOrWhiteSpace(action)
                && action is not ("login" or "security" or "draft" or "approval" or "application"))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: "Invalid activity action filter.");
            }

            var caller = CallerContext.Require(user);
            var activity = await service.GetRecentActivityAsync(
                caller.UserId,
                Math.Clamp(limit ?? 10, 1, 50),
                from,
                to,
                action,
                cancellationToken);
            return Results.Ok(activity);
        })
        .WithName("GetAccountActivity");

        group.MapGet("/me/loans", async (
            ClaimsPrincipal user,
            int? limit,
            AccountService service,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var loans = await service.GetProcessedLoansAsync(
                caller.UserId,
                Math.Clamp(limit ?? 10, 1, 50),
                cancellationToken);
            return Results.Ok(loans);
        })
        .WithName("GetAccountLoans");

        group.MapGet("/me/clients", async (
            ClaimsPrincipal user,
            int? limit,
            AccountService service,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var clients = await service.GetRecentClientsAsync(
                caller.UserId,
                Math.Clamp(limit ?? 5, 1, 50),
                cancellationToken);
            return Results.Ok(clients);
        })
        .WithName("GetAccountClients");

        group.MapGet("/me/sessions", async (
            ClaimsPrincipal user,
            AccountService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var current = await ResolveCurrentSessionIdAsync(service, caller.UserId, httpContext, cancellationToken);
            var sessions = await service.GetSessionsAsync(caller.UserId, current, cancellationToken);
            return Results.Ok(new
            {
                items = sessions,
                currentPage = 1,
                pageSize = Math.Max(sessions.Count, 1),
                totalCount = sessions.Count,
                totalPages = 1,
                hasPreviousPage = false,
                hasNextPage = false,
            });
        })
        .WithName("GetAccountSessions");

        group.MapDelete("/me/sessions/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            AccountService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var current = await ResolveCurrentSessionIdAsync(service, caller.UserId, httpContext, cancellationToken);
            var result = await service.RevokeSessionAsync(caller.UserId, id, current, cancellationToken);
            return result switch
            {
                SessionRevokeResult.Revoked => Results.NoContent(),
                SessionRevokeResult.CurrentSession => Results.BadRequest(),
                _ => Results.NotFound(),
            };
        })
        .RequireRateLimiting("write")
        .WithName("RevokeAccountSession");

        group.MapDelete("/me/sessions/others", async (
            ClaimsPrincipal user,
            AccountService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var current = await ResolveCurrentSessionIdAsync(service, caller.UserId, httpContext, cancellationToken);
            var revokedCount = await service.RevokeOtherSessionsAsync(caller.UserId, current, cancellationToken);
            return Results.Ok(new { revokedCount });
        })
        .RequireRateLimiting("write")
        .WithName("RevokeOtherAccountSessions");
    }

    private static async Task<Guid?> ResolveCurrentSessionIdAsync(
        AccountService service,
        Guid userId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var raw = RefreshCookie.Read(httpContext);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return await service.FindSessionIdByRawTokenAsync(userId, raw, cancellationToken);
    }
}

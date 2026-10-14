using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;

namespace Ebi.Alas.Api.Features.Account;

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

        group.MapGet("/me/activity", async (
            ClaimsPrincipal user,
            int? limit,
            AccountService service,
            CancellationToken cancellationToken) =>
        {
            var caller = CallerContext.Require(user);
            var activity = await service.GetRecentActivityAsync(
                caller.UserId,
                Math.Clamp(limit ?? 10, 1, 50),
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
    }
}

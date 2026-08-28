using System.Security.Claims;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace EBI.ALAS.Api.Features.Account;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/account")
            .WithTags("Account")
            .RequireAuthorization();

        // GET /api/account/me
        group.MapGet("/me", async (ClaimsPrincipal principal, IAccountService accountService) =>
        {
            var userId = principal.GetUserId();
            var profile = await accountService.GetProfileAsync(userId);
            
            return profile == null
                ? Results.NotFound(ApiResponse.ErrorResponse("Profile not found"))
                : Results.Ok(ApiResponse<AccountProfileResponse>.SuccessResponse(profile));
        })
        .WithName("GetAccountProfile")
        .Produces<ApiResponse<AccountProfileResponse>>(200)
        .Produces<ApiResponse>(404);

        // PUT /api/account/me
        group.MapPut("/me", async (
            ClaimsPrincipal principal,
            [FromBody] UpdateProfileRequest request,
            IValidator<UpdateProfileRequest> validator,
            IAccountService accountService) =>
        {
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    "Validation failed", 
                    errors.SelectMany(e => e.Value).ToList()));
            }

            var userId = principal.GetUserId();
            var success = await accountService.UpdateProfileAsync(userId, request);

            return success
                ? Results.Ok(ApiResponse.SuccessResponse("Profile updated successfully"))
                : Results.NotFound(ApiResponse.ErrorResponse("Profile not found"));
        })
        .WithName("UpdateAccountProfile")
        .Produces<ApiResponse>(200)
        .Produces<ApiResponse>(400)
        .Produces<ApiResponse>(404);

        // GET /api/account/me/sessions
        group.MapGet("/me/sessions", async (
            ClaimsPrincipal principal,
            IAccountService accountService,
            [AsParameters] SessionsQueryParameters parameters) =>
        {
            var userId = principal.GetUserId();
            var jti = principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti) ?? "";
            var sessions = await accountService.GetActiveSessionsAsync(userId, jti, parameters.PageNumber, parameters.PageSize);
            
            return Results.Ok(ApiResponse<PagedSessionsResponse>.SuccessResponse(sessions));
        })
        .WithName("GetAccountSessions")
        .Produces<ApiResponse<PagedSessionsResponse>>(200);

        // DELETE /api/account/me/sessions/{id}
        group.MapDelete("/me/sessions/{id:int}", async (
            int id,
            ClaimsPrincipal principal,
            IAccountService accountService) =>
        {
            var userId = principal.GetUserId();
            var success = await accountService.RevokeSessionAsync(userId, id);

            return success
                ? Results.Ok(ApiResponse.SuccessResponse("Session revoked successfully"))
                : Results.NotFound(ApiResponse.ErrorResponse("Session not found or already revoked"));
        })
        .WithName("RevokeAccountSession")
        .Produces<ApiResponse>(200)
        .Produces<ApiResponse>(404);

        // GET /api/account/me/activity
        group.MapGet("/me/activity", async (
            ClaimsPrincipal principal,
            IAccountService accountService,
            [AsParameters] ActivityQueryParameters parameters) =>
        {
            var userId = principal.GetUserId();
            var activity = await accountService.GetRecentActivityAsync(userId, parameters.Limit);
            
            return Results.Ok(ApiResponse<List<ActivityResponse>>.SuccessResponse(activity));
        })
        .WithName("GetAccountActivity")
        .Produces<ApiResponse<List<ActivityResponse>>>(200);

        // GET /api/account/me/loans
        group.MapGet("/me/loans", async (
            ClaimsPrincipal principal,
            IAccountService accountService,
            [AsParameters] LoansQueryParameters parameters) =>
        {
            var userId = principal.GetUserId();
            var loans = await accountService.GetProcessedLoansAsync(userId, parameters.Limit);
            
            return Results.Ok(ApiResponse<List<ProcessedLoanResponse>>.SuccessResponse(loans));
        })
        .WithName("GetAccountLoans")
        .Produces<ApiResponse<List<ProcessedLoanResponse>>>(200);

        // GET /api/account/me/clients
        group.MapGet("/me/clients", async (
            ClaimsPrincipal principal,
            IAccountService accountService,
            [AsParameters] ClientsQueryParameters parameters) =>
        {
            var userId = principal.GetUserId();
            var clients = await accountService.GetRecentClientsAsync(userId, parameters.Limit);
            
            return Results.Ok(ApiResponse<List<RecentClientResponse>>.SuccessResponse(clients));
        })
        .WithName("GetAccountClients")
        .Produces<ApiResponse<List<RecentClientResponse>>>(200);
    }
}

public record ActivityQueryParameters(int Limit = 10);
public record LoansQueryParameters(int Limit = 10);
public record ClientsQueryParameters(int Limit = 5);
public record SessionsQueryParameters(int PageNumber = 1, int PageSize = 10);
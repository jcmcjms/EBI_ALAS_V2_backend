using Alas.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace Alas.Api.Endpoints;

public static class AuthEndpoints
{
   public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
   {
      var group = app.MapGroup("/api/v1/auth");

      group.MapPost("/login", Login)
         .AllowAnonymous()
         .RequireRateLimiting("auth")
         .WithName("Login");

      group.MapPost("/refresh", Refresh)
         .AllowAnonymous()
         .RequireRateLimiting("auth")
         .WithName("RefreshToken");

      group.MapPost("/logout", Logout)
         .RequireAuthorization()
         .WithName("Logout");

      return app;
   }

   private static async Task<IResult> Login(
      [FromBody] LoginRequest request,
      AuthService authService,
      HttpContext httpContext,
      CancellationToken cancellationToken)
   {
      var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
      var userAgent = httpContext.Request.Headers.UserAgent.ToString();

      var result = await authService.LoginAsync(
         request,
         ipAddress,
         userAgent,
         cancellationToken);

      return result is null
         ? Results.Unauthorized()
         : Results.Ok(result);
   }

   private static async Task<IResult> Refresh(
      [FromBody] RefreshRequest request,
      AuthService authService,
      HttpContext httpContext,
      CancellationToken cancellationToken)
   {
      var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
      var userAgent = httpContext.Request.Headers.UserAgent.ToString();

      var result = await authService.RefreshAsync(
         request,
         ipAddress,
         userAgent,
         cancellationToken);

      return result is null
         ? Results.Unauthorized()
         : Results.Ok(result);
   }

   private static async Task<IResult> Logout(
      [FromBody] RefreshRequest request,
      AuthService authService,
      CancellationToken cancellationToken)
   {
      await authService.LogoutAsync(request.RefreshToken, cancellationToken);

      return Results.NoContent();
   }
}
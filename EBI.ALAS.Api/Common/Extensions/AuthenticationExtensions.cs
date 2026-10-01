using System.Security.Claims;
using System.Text;
using EBI.ALAS.Api.Features.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
namespace EBI.ALAS.Api.Common.Extensions;
public static class AuthenticationExtensions
{
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = !IsDevelopment();
            options.SaveToken = true;
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
                ClockSkew = TimeSpan.Zero,
                NameClaimType = "username"
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = HandleSignalRTokenExtraction,
                OnTokenValidated = HandleJtiRevocationCheck
            };
        });
        return services;
    }
    private static Task HandleSignalRTokenExtraction(MessageReceivedContext context)
    {
        var accessToken = context.Request.Query["access_token"];
        var path = context.HttpContext.Request.Path;
        if (!string.IsNullOrEmpty(accessToken)
            && path.StartsWithSegments("/hubs/notifications"))
        {
            context.Token = accessToken;
        }
        return Task.CompletedTask;
    }
    private static async Task HandleJtiRevocationCheck(TokenValidatedContext context)
    {
        var jti = context.Principal?.FindFirstValue(
            System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti);
        if (string.IsNullOrEmpty(jti))
        {
            context.Fail("Token missing JTI claim");
            return;
        }
        var tokenRevocationRepo = context.HttpContext.RequestServices
            .GetRequiredService<ITokenRevocationRepository>();
        if (await tokenRevocationRepo.IsTokenRevokedAsync(jti))
            context.Fail("Token has been revoked");
    }
    private static bool IsDevelopment()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }
}

using EBI.ALAS.Api.Common.Authorization;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Dashboard;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.Users;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;

namespace EBI.ALAS.Api.Common.Extensions;

/// <summary>
/// Extension methods for registering application services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all application services, repositories, and infrastructure components.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // ─── Data Access ─────────────────────────────────────────────────
        services.AddScoped<AppDbContext>();

        // ─── Auth Services ───────────────────────────────────────────────
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<ITokenRevocationRepository, TokenRevocationRepository>();

        // ─── Loan Services ───────────────────────────────────────────────
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddScoped<ILoanWorkflowService, LoanWorkflowService>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IFormNumberGenerator, FormNumberGenerator>();

        // ─── Dashboard Services ──────────────────────────────────────────
        services.AddScoped<IDashboardService, DashboardService>();

        // ─── User Management Services ────────────────────────────────────
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserService, UserService>();

        // ─── Authorization ───────────────────────────────────────────────
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return services;
    }
}

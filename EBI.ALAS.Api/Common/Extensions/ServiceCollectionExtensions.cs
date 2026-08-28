using EBI.ALAS.Api.Common.Authorization;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Account;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Branches;
using EBI.ALAS.Api.Features.Dashboard;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.Users;
using EBI.ALAS.Api.Features.WebLoans;
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
        // ─── Common Services ──────────────────────────────────────────────
        services.AddSingleton<ITimeProvider, PhilippinesTimeProvider>();

        // ─── Data Access ─────────────────────────────────────────────────
        services.AddScoped<AppDbContext>();

        // ─── Auth Services ───────────────────────────────────────────────
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<ITokenRevocationRepository, TokenRevocationRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

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

        // ─── Branch Services ──────────────────────────────────────────────
        services.AddScoped<IBranchRepository, BranchRepository>();
        services.AddScoped<IBranchService, BranchService>();

        // ─── WebLoan Integration (read-only) ─────────────────────────────
        services.AddScoped<IWebLoanService, WebLoanService>();

        // ─── Account Services (My Account page) ──────────────────────────
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IAccountService, AccountService>();

        // ─── Authorization ───────────────────────────────────────────────
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return services;
    }
}

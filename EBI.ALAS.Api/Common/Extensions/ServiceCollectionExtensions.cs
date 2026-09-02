using EBI.ALAS.Api.Common.Authorization;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Account;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.AuditLogs;
using EBI.ALAS.Api.Features.Branches;
using EBI.ALAS.Api.Features.Dashboard;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.Users;
using EBI.ALAS.Api.Features.WebLoans;
using EBI.ALAS.Api.Infrastructure.Data;
using EBI.ALAS.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace EBI.ALAS.Api.Common.Extensions;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // ─── Common Services ──────────────────────────────────────────────
        services.AddSingleton<ITimeProvider, PhilippinesTimeProvider>();

        // ─── In-process cache ────────────────────────────────────────────
        // IMemoryCache backs the JTI blacklist hot path. Must be registered
        // before any auth pipeline that resolves ITokenRevocationRepository.
        services.AddMemoryCache();

        // ─── Data Access ─────────────────────────────────────────────────
        services.AddScoped<AppDbContext>();

        // ─── Auth Services ───────────────────────────────────────────────
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        // Durable inner implementation. Registered by concrete type so the
        // decorator (below) can resolve it without an infinite-recursion guard.
        services.AddScoped<TokenRevocationRepository>();
        // Hot-path: every authenticated request resolves the caching decorator.
        services.AddScoped<ITokenRevocationRepository>(sp =>
            new CachingTokenRevocationRepository(
                sp.GetRequiredService<TokenRevocationRepository>(),
                sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                sp.GetRequiredService<ITimeProvider>(),
                sp.GetRequiredService<ILogger<CachingTokenRevocationRepository>>()));
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

        // ─── WebLoan Services (read-only integration with legacy DB) ──
        services.AddScoped<IWebLoanRepository, WebLoanRepository>();
        services.AddScoped<IWebLoanService, WebLoanService>();

        // ─── Account Services (My Account page) ──────────────────────────
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IAccountService, AccountService>();

        // ─── Audit Log Services ─────────────────────────────────────────
        services.AddScoped<IAuditLogService, AuditLogService>();

        // ─── Authorization ───────────────────────────────────────────────
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return services;
    }
}

using EBI.ALAS.Api.Common.Authorization;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Account;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.AuditLogs;
using EBI.ALAS.Api.Features.Branches;
using EBI.ALAS.Api.Features.Dashboard;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Features.Users;
using EBI.ALAS.Api.Features.WebLoans;
using EBI.ALAS.Api.Infrastructure.Data;
using EBI.ALAS.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace EBI.ALAS.Api.Common.Extensions;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // ─── Common Services ──────────────────────────────────────────────
        services.AddSingleton<ITimeProvider, PhilippinesTimeProvider>();

        // ─── In-process cache ────────────────────────────────────────────
        // IMemoryCache backs the dashboard summary cache and the branch
        // cache. Per-pod cache effectiveness is acceptable for these —
        // they're read-mostly aggregates where staleness across pods is
        // tolerable (a 30s TTL hides any consistency gap).
        //
        // IMPORTANT: the JTI blacklist (CachingTokenRevocationRepository)
        // is NOT in IMemoryCache. The blacklist MUST be cross-pod
        // coherent (otherwise a token revoked on pod A is still
        // accepted on pod B for up to 15 min — a real auth bypass on
        // multi-replica deployments). The blacklist lives in
        // IDistributedCache (Redis) — see the AddDistributedCache
        // registration below.
        //
        // SizeLimit gives us a hard ceiling so a malicious caller can't
        // blow up the server's working set with millions of unique
        // cache keys. The dashboard entries declare Size=1 each; we
        // cap at 10k entries which is well above any realistic
        // (branch × role) combination.
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 10_000;
        });

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
        // The decorator reads IDistributedCache (Redis) so every replica sees
        // the same JTI blacklist. Critical: a revoked token on pod A must be
        // rejected on pod B within milliseconds — otherwise it's a real auth
        // bypass on multi-replica deployments.
        services.AddScoped<ITokenRevocationRepository>(sp =>
            new CachingTokenRevocationRepository(
                sp.GetRequiredService<TokenRevocationRepository>(),
                sp.GetRequiredService<IDistributedCache>(),
                sp.GetRequiredService<ITimeProvider>(),
                sp.GetRequiredService<ILogger<CachingTokenRevocationRepository>>()));
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        // Hourly cleanup of expired refresh tokens + expired JTI
        // revocations. Without this, both tables grow forever (one row
        // per login, one row per logout / refresh-with-revocation /
        // change-password). Cadence is configurable via
        // `TokenCleanup:IntervalMinutes` in appsettings (default 60).
        services.AddHostedService<CleanupExpiredTokensHostedService>();

        // ─── Loan Services ───────────────────────────────────────────────
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddScoped<ILoanWorkflowService, LoanWorkflowService>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<ILamIdGenerator, LamIdGenerator>();
        services.AddScoped<ILoanSubmissionService, LoanSubmissionService>();

        // Loan product catalog (ALAS-owned mirror of webloan.loan_product).
        // Repository is scoped (uses AppDbContext). Service is scoped.
        // Sync service is scoped — depends on the scoped repository.
        services.AddScoped<ILoanProductRepository, LoanProductRepository>();
        services.AddScoped<ILoanProductSyncService, LoanProductSyncService>();
        services.AddScoped<ILoanProductService, LoanProductService>();

        // Background job that runs the sync on a configurable interval.
        // Hosted services are singletons by ASP.NET Core convention.
        services.AddHostedService<LoanProductSyncHostedService>();

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

        // ─── Notification Services ───────────────────────────────────────
        // Per-user inbox read by the SPA header bell (GET /api/notifications)
        // and written by workflow handlers on status transitions.
        services.AddScoped<INotificationService, NotificationService>();

        // ─── Authorization ───────────────────────────────────────────────
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return services;
    }
}

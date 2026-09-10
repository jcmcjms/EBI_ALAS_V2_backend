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
using Microsoft.Extensions.Caching.Memory;

namespace EBI.ALAS.Api.Common.Extensions;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // ─── Common Services ──────────────────────────────────────────────
        services.AddSingleton<ITimeProvider, PhilippinesTimeProvider>();

        // ─── In-process cache ────────────────────────────────────────────
        // IMemoryCache backs three consumers:
        //   1. Dashboard summary cache and the branch cache —
        //      read-mostly aggregates where a 30s TTL hides any
        //      consistency gap.
        //   2. JTI revocation blacklist (CachingTokenRevocationRepository) —
        //      short-lived, per-request. A revoked token must be
        //      rejected on the same pod for the rest of its 15-min
        //      access-token window.
        //   3. Idempotency middleware — replayed POST/PUT/PATCH
        //      responses cached for 90s.
        //
        // All three are per-process by design. The deployment
        // topology is single-pod; see README §"Cache topology &
        // limits" for the trade-off.
        //
        // SizeLimit gives us a hard ceiling so a malicious caller
        // can't blow up the server's working set with millions of
        // unique cache keys. Each consumer declares Size on its
        // entries (see CachingTokenRevocationRepository /
        // IdempotencyMiddleware) so the LRU policy can evict under
        // pressure. 10k entries is well above any realistic working
        // set for the three combined.
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
        // The decorator reads IMemoryCache so revoked JTIs are rejected
        // within the same process for the rest of the access-token window.
        // Per-process — single-pod deployment assumption documented in
        // README §"Cache topology & limits".
        services.AddScoped<ITokenRevocationRepository>(sp =>
            new CachingTokenRevocationRepository(
                sp.GetRequiredService<TokenRevocationRepository>(),
                sp.GetRequiredService<IMemoryCache>(),
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

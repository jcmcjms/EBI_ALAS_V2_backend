using EBI.ALAS.Api.Common.Authorization;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Account;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Presence;
using EBI.ALAS.Api.Features.AuditLogs;
using EBI.ALAS.Api.Features.Branches;
using EBI.ALAS.Api.Features.Dashboard;
using EBI.ALAS.Api.Features.Loans;
using Microsoft.Extensions.Caching.Distributed;
using EBI.ALAS.Api.Features.Loans.Computation;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Features.SystemSettings;
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
        // Common Services
        services.AddSingleton<ITimeProvider, PhilippinesTimeProvider>();

        // In-process cache
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
        // All three are per-process by design. The deployment
        // topology is single-pod; see README §"Cache topology &
        // limits" for the trade-off.
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

        // Data Access
        services.AddScoped<AppDbContext>();

        // Auth Services
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
                sp.GetRequiredService<IDistributedCache>(),
                sp.GetRequiredService<ITimeProvider>(),
                sp.GetRequiredService<ILogger<CachingTokenRevocationRepository>>()));
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuthService, AuthService>();

        // Hourly cleanup of expired refresh tokens + expired JTI
        // revocations. Without this, both tables grow forever (one row
        // per login, one row per logout / refresh-with-revocation /
        // change-password). Cadence is configurable via
        // `TokenCleanup:IntervalMinutes` in appsettings (default 60).
        services.AddHostedService<CleanupExpiredTokensHostedService>();

        // Loan Services
        services.AddSingleton<IWorkflowConfiguration, WorkflowConfiguration>();
        // Pure math engine — stateless, no I/O. Singleton keeps one instance
        // for the app lifetime. Product configs come from the cached catalog.
        services.AddSingleton<ILoanComputationService, LoanComputationService>();
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddScoped<ILoanWorkflowService, LoanWorkflowService>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<ILamIdGenerator, LamIdGenerator>();
        services.AddScoped<ILoanSubmissionService, LoanSubmissionService>();

        // Workflow Queue (materialized-head FIFO desk)
        // Server-owned queue that enforces FIFO ordering and deterministic
        // ownership for review desk transitions.
        services.AddScoped<IWorkflowQueueService, WorkflowQueueService>();

        // Document Gate (automatic hold/release for incomplete docs)
        // Centralizes the ForIncompleteDocuments lifecycle so queue membership
        // is always derived from the document server.
        services.AddScoped<IDocumentGateService, DocumentGateService>();
        services.AddScoped<ISystemPrincipal, SystemPrincipal>();

        // Extracted transition logic shared by single-loan and group endpoints.
        services.AddScoped<ILoanStatusTransitionService, LoanStatusTransitionService>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        // Checklist documents from BPB_BINARY_SERVER (read-only integration).
        services.AddScoped<IChecklistDocumentRepository, ChecklistDocumentRepository>();

        // Per-item document requirement status tracking (Missing/Pending/Submitted/Verified).
        services.AddScoped<IDocumentChecklistStore, DocumentChecklistStore>();

        // Loan product catalog (ALAS-owned mirror of webloan.loan_product).
        // Repository is scoped (uses AppDbContext). Service is scoped.
        // Sync service is scoped — depends on the scoped repository.
        services.AddScoped<ILoanProductRepository, LoanProductRepository>();
        services.AddScoped<ILoanProductSyncService, LoanProductSyncService>();
        services.AddScoped<ILoanProductService, LoanProductService>();
        services.AddScoped<ILoanProductImportService, LoanProductImportService>();

        // Background job that runs the sync on a configurable interval.
        // Hosted services are singletons by ASP.NET Core convention.
        services.AddHostedService<LoanProductSyncHostedService>();

        // System Settings (DB-backed workflow flags)
        // Scoped store — uses AppDbContext. The hosted service (singleton)
        // creates a scope per tick so it never captures a captive context.
        services.AddScoped<ISystemSettingsStore, SystemSettingsStore>();

        // Propagates DB overrides written by OTHER instances within 30s.
        // Same pattern as LoanProductSyncHostedService.
        services.AddHostedService<WorkflowSettingsRefreshHostedService>();

        // Dashboard Services
        services.AddScoped<IDashboardService, DashboardService>();

        // User Management Services
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IUserImportService, UserImportService>();
        services.AddSingleton<ITempPasswordGenerator, TempPasswordGenerator>();

        // Branch Services
        services.AddScoped<IBranchRepository, BranchRepository>();
        services.AddScoped<IBranchService, BranchService>();

        // WebLoan Services (read-only integration with legacy DB)
        services.AddScoped<IWebLoanRepository, WebLoanRepository>();
        services.AddScoped<IWebLoanService, WebLoanService>();

        // Account Services (My Account page)
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IAccountService, AccountService>();

        // Audit Log Services
        services.AddScoped<IAuditLogService, AuditLogService>();

        // Notification Services
        // Per-user inbox read by the SPA header bell (GET /api/notifications)
        // and written by workflow handlers on status transitions.
        services.AddScoped<INotificationService, NotificationService>();

        // Real-time Notifications (SignalR)
        // Push transport for instant bell updates + toasts. Business logic
        // depends on the abstraction (IRealtimeNotificationService) so the
        // hub can be swapped or mocked without touching loan code.
        services.AddScoped<IRealtimeNotificationService, RealtimeNotificationService>();

        // In-memory presence registry (single-pod topology).
        services.AddSingleton<IPresenceService, PresenceService>();
        // Entity watch — reusable "who is looking at this record" primitive.
        services.AddSingleton<IEntityWatchService, EntityWatchService>();
        // Pure routing function — reads approval matrix from DB, cached 5min.
        services.AddScoped<IApprovalRoutingService, ApprovalRoutingService>();
        // Document completeness gate — checks checklist documents against requirements.
        services.AddScoped<IDocumentCompletenessService, DocumentCompletenessService>();
        // Lease-based assignment — assigns loans to available approvers.
        services.AddScoped<ILoanAssignmentService, LoanAssignmentService>();

        // Background job that re-verifies document completeness for in-flight
        // loans. Keeps the stamp honest for legacy rows and post-stamp drift.
        // Same pattern as LoanProductSyncHostedService.
        services.AddHostedService<DocumentCompletenessSyncHostedService>();

        // Background job that reconciles the workflow queue every 5 minutes.
        // Promotes heads of partitions with no Active item and dequeues stale
        // items whose loan status no longer matches their stage.
        services.AddHostedService<QueueReconciliationHostedService>();

        // Authorization
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // Branch Scope (single source of truth for branch scoping)
        // Fail-safe service that guarantees no empty branch sets reach
        // SQL predicates. Consulted by webloan drill-downs and any
        // future branch-scoped read endpoint.
        services.AddScoped<IBranchScopeService, BranchScopeService>();

        return services;
    }
}

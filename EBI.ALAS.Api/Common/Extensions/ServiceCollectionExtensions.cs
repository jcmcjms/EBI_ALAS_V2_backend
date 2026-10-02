using EBI.ALAS.Api.Shared.Authorization;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Account;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Presence;
using EBI.ALAS.Api.Features.AuditLogs;
using EBI.ALAS.Api.Features.Branches;
using EBI.ALAS.Api.Features.Dashboard;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Infrastructure.Caching;
using EBI.ALAS.Api.Features.Loans.Computation;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Features.SystemSettings;
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
        services.AddSingleton<ITimeProvider, PhilippinesTimeProvider>();
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 10_000;
        });
        services.AddScoped<AppDbContext>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<TokenRevocationRepository>();
        services.AddScoped<ITokenRevocationRepository>(sp =>
        {
            var multiplexer = sp.GetService<GarnetConnectionMultiplexer>();
            if (multiplexer is not null)
            {
                return new CachingTokenRevocationRepository(
                    sp.GetRequiredService<TokenRevocationRepository>(),
                    sp.GetRequiredService<IMemoryCache>(),
                    multiplexer,
                    sp.GetRequiredService<ITimeProvider>(),
                    sp.GetRequiredService<ILogger<CachingTokenRevocationRepository>>());
            }
            return new CachingTokenRevocationRepository(
                sp.GetRequiredService<TokenRevocationRepository>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<IDistributedCache>(),
                sp.GetRequiredService<ITimeProvider>(),
                sp.GetRequiredService<ILogger<CachingTokenRevocationRepository>>());
        });
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddHostedService<CleanupExpiredTokensHostedService>();
        services.AddSingleton<IWorkflowConfiguration, WorkflowConfiguration>();
        services.AddSingleton<ILoanComputationService, LoanComputationService>();
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddScoped<ILoanWorkflowService, LoanWorkflowService>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<ISignatureChainService, SignatureChainService>();
        services.AddScoped<ILamIdGenerator, LamIdGenerator>();
        services.AddScoped<ILoanSubmissionService, LoanSubmissionService>();
        services.AddScoped<IWorkflowQueueService, WorkflowQueueService>();
        services.AddScoped<IDocumentGateService, DocumentFlagService>();
        services.AddScoped<ISystemPrincipal, SystemPrincipal>();
        services.AddScoped<ILoanStatusTransitionService, LoanStatusTransitionService>();
        services.AddScoped<IChecklistDocumentRepository, ChecklistDocumentRepository>();
        services.AddScoped<IDocumentChecklistStore, DocumentChecklistStore>();
        services.AddScoped<ILoanProductRepository, LoanProductRepository>();
        services.AddScoped<ILoanProductSyncService, LoanProductSyncService>();
        services.AddScoped<ILoanProductService, LoanProductService>();
        services.AddScoped<ILoanProductImportService, LoanProductImportService>();
        services.AddHostedService<LoanProductSyncHostedService>();
        services.AddScoped<ISystemSettingsStore, SystemSettingsStore>();
        services.AddHostedService<WorkflowSettingsRefreshHostedService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IUserImportService, UserImportService>();
        services.AddSingleton<ITempPasswordGenerator, TempPasswordGenerator>();
        services.AddScoped<IBranchRepository, BranchRepository>();
        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<IWebLoanRepository, WebLoanRepository>();
        services.AddScoped<IWebLoanService, WebLoanService>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IRealtimeNotificationService, RealtimeNotificationService>();
        services.AddScoped<IRemarkNotificationService, RemarkNotificationService>();
        services.AddSingleton<IPresenceService, PresenceService>();
        services.AddSingleton<IEntityWatchService, EntityWatchService>();
        services.AddScoped<IApprovalRoutingService, ApprovalRoutingService>();
        services.AddScoped<IDocumentCompletenessService, DocumentCompletenessService>();
        services.AddScoped<ILoanAssignmentService, LoanAssignmentService>();
        services.AddHostedService<DocumentCompletenessSyncHostedService>();
        services.AddHostedService<QueueReconciliationHostedService>();
        services.AddHostedService<DisbursementSyncHostedService>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IBranchScopeService, BranchScopeService>();
        return services;
    }
}

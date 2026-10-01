using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Features.WebLoans;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
namespace EBI.ALAS.Api.Features.Loans;
public sealed class DisbursementSyncHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DisbursementSyncHostedService> _logger;
    public DisbursementSyncHostedService(
        IServiceScopeFactory scopes,
        ILogger<DisbursementSyncHostedService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMinutes(2);
        _logger.LogInformation(
            "DisbursementSyncHostedService started. Interval: {IntervalMinutes} minutes.",
            interval.TotalMinutes);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await SyncAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Disbursement sync cycle failed.");
            }
            try
            {
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
    private async Task SyncAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var webLoanRepo = scope.ServiceProvider.GetRequiredService<IWebLoanRepository>();
        var workflowService = scope.ServiceProvider.GetRequiredService<ILoanWorkflowService>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();
        var realtimeService = scope.ServiceProvider.GetRequiredService<IRealtimeNotificationService>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<ITimeProvider>();
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        var loans = await db.LoanApplications
            .Where(l => l.Status == "Approved" || l.Status == "ForDisbursement")
            .ToListAsync(ct);
        if (loans.Count == 0)
            return;
        _logger.LogDebug("Checking {Count} loan(s) for disbursement sync.", loans.Count);
        var systemUserId = await ResolveSystemUserIdAsync(db, cache, ct);
        var synced = 0;
        foreach (var loan in loans)
        {
            if (string.IsNullOrWhiteSpace(loan.LoanNo))
                continue;
            try
            {
                var preLoan = await webLoanRepo.GetPreLoanDataByLoanNoAsync(loan.LoanNo, ct);
                if (preLoan is null)
                    continue;
                string? targetStatus = null;
                string? reason = null;
                if (preLoan.ReleasedDate is not null && !string.IsNullOrWhiteSpace(preLoan.ReleasedBy)
                    && loan.Status == "ForDisbursement")
                {
                    targetStatus = "Disbursed";
                    reason = $"Auto-synced: released on {preLoan.ReleasedDate:yyyy-MM-dd} by {preLoan.ReleasedBy}.";
                }
                else if (preLoan.ApprovedDate is not null && !string.IsNullOrWhiteSpace(preLoan.ApprovedBy)
                    && loan.Status == "Approved")
                {
                    targetStatus = "ForDisbursement";
                    reason = $"Auto-synced: approved on {preLoan.ApprovedDate:yyyy-MM-dd} by {preLoan.ApprovedBy}.";
                }
                if (targetStatus is null)
                    continue;
                if (!workflowService.IsValidTransition(loan.Status, targetStatus, Roles.Admin))
                    continue;
                var fromStatus = loan.Status;
                var strategy = db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    loan.Status = targetStatus!;
                    loan.LastActionDate = timeProvider.UtcNow;
                    await db.SaveChangesAsync(ct);
                    await auditLogger.LogActionAsync(
                        loan.Id, systemUserId, "StatusChanged", fromStatus, targetStatus, reason);
                    await tx.CommitAsync(ct);
                });
                await realtimeService.NotifyDashboardUpdateAsync(loan.BranchCode);
                synced++;
                _logger.LogInformation(
                    "Auto-synced loan {LamId} ({LoanNo}): {From} → {To}",
                    loan.LamId, loan.LoanNo, fromStatus, targetStatus);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to sync loan {LamId} ({LoanNo}). Skipping.",
                    loan.LamId, loan.LoanNo);
            }
        }
        if (synced > 0)
            _logger.LogInformation("Disbursement sync complete: {Synced}/{Total} loan(s) updated.", synced, loans.Count);
    }
    private static async Task<int> ResolveSystemUserIdAsync(
        AppDbContext db, IMemoryCache cache, CancellationToken ct)
    {
        const string cacheKey = "system:userId";
        if (cache.TryGetValue<int>(cacheKey, out var id))
            return id;
        id = await db.Users.AsNoTracking()
            .Where(u => u.Username == "system")
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);
        if (id == 0)
            throw new InvalidOperationException(
                "System user not found. Ensure DbInitializer has seeded a user with Username == 'system'.");
        cache.Set(cacheKey, id, TimeSpan.FromHours(1));
        return id;
    }
}

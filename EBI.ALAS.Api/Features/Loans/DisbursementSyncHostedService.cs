using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Shared.Time;
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
    private DateTime _lastSyncUtc = DateTime.MinValue;
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
        var realtimeService = scope.ServiceProvider.GetRequiredService<IRealtimeNotificationService>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<ITimeProvider>();
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        var cycleStart = timeProvider.UtcNow;
        var loans = await db.LoanApplications
            .Where(l => (l.Status == "Approved" || l.Status == "ForDisbursement")
                     && l.LastActionDate >= _lastSyncUtc)
            .ToListAsync(ct);
        if (loans.Count == 0)
            return;

        var loanNos = loans
            .Where(l => !string.IsNullOrWhiteSpace(l.LoanNo))
            .Select(l => l.LoanNo!)
            .Distinct()
            .ToList();

        var preLoanData = await webLoanRepo.GetPreLoanDataByLoanNosAsync(loanNos, ct);

        _logger.LogDebug("Checking {Count} loan(s) for disbursement sync.", loans.Count);
        var systemUserId = await ResolveSystemUserIdAsync(db, cache, ct);
        var synced = 0;
        var branchesNotified = new HashSet<string>(StringComparer.Ordinal);

        // Collect all changes
        var changes = new List<(LoanApplication Loan, string TargetStatus, string Reason, string FromStatus)>();
        foreach (var loan in loans)
        {
            if (string.IsNullOrWhiteSpace(loan.LoanNo))
                continue;
            if (!preLoanData.TryGetValue(loan.LoanNo, out var preLoan))
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

            if (targetStatus is not null && workflowService.IsValidTransition(loan.Status, targetStatus, Roles.Admin))
            {
                changes.Add((loan, targetStatus, reason!, loan.Status));
            }
        }

        // Apply all changes in a single transaction
        if (changes.Count > 0)
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var now = timeProvider.UtcNow;

                foreach (var (loan, targetStatus, _, _) in changes)
                {
                    loan.Status = targetStatus;
                    loan.LastActionDate = now;
                }
                await db.SaveChangesAsync(ct);

                // Batch audit records
                var auditActions = changes.Select(c => new LoanAction
                {
                    LoanApplicationId = c.Loan.Id,
                    ActionByUserId = systemUserId,
                    Action = "StatusChanged",
                    FromStatus = c.FromStatus,
                    ToStatus = c.TargetStatus,
                    Comments = c.Reason,
                    ActionDate = now,
                }).ToList();
                db.LoanActions.AddRange(auditActions);
                await db.SaveChangesAsync(ct);

                await tx.CommitAsync(ct);
            });

            foreach (var (loan, targetStatus, _, fromStatus) in changes)
            {
                branchesNotified.Add(loan.BranchCode);
                _logger.LogInformation(
                    "Auto-synced loan {LamId} ({LoanNo}): {From} \u2192 {To}",
                    loan.LamId, loan.LoanNo, fromStatus, targetStatus);
            }
            synced = changes.Count;
        }

        // Deduplicate branch notifications
        foreach (var branch in branchesNotified)
        {
            await realtimeService.NotifyDashboardUpdateAsync(branch);
        }

        // Update watermark
        _lastSyncUtc = cycleStart;

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
        cache.Set(cacheKey, id, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
            Size = 1,
        });
        return id;
    }
}

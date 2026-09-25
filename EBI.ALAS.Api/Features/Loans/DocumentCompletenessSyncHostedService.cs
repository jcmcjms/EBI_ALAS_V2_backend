using Microsoft.EntityFrameworkCore;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Infrastructure.Data;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Keeps LoanApplication.DocumentsCompleteAt honest. The stamp is a cache of
/// the external document server; caches need a refresh strategy. This sweep
/// re-verifies in-flight loans and writes ONLY on state change, so a quiet
/// queue costs zero UPDATEs.
///
/// Also syncs document flags: when a flagged loan becomes document-complete,
/// the flag columns are cleared via IDocumentGateService.SyncAsync.
/// </summary>
public sealed class DocumentCompletenessSyncHostedService : BackgroundService
{
    private static readonly string[] ActiveStatuses =
        ["ForRecommendation", "ForChecking", "ForApproval", "ForRevision"];

    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<DocumentCompletenessSyncHostedService> _logger;

    public DocumentCompletenessSyncHostedService(
        IServiceScopeFactory scopes, IConfiguration config,
        ILogger<DocumentCompletenessSyncHostedService> logger)
    { _scopes = scopes; _config = config; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMinutes(
            _config.GetValue("DocumentCompleteness:IntervalMinutes", 5));

        _logger.LogInformation(
            "DocumentCompletenessSyncHostedService started. Interval: {IntervalMinutes} minutes.",
            interval.TotalMinutes);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var completeness = scope.ServiceProvider.GetRequiredService<IDocumentCompletenessService>();
                var time = scope.ServiceProvider.GetRequiredService<ITimeProvider>();

                var loans = await db.LoanApplications.AsNoTracking()
                    .Where(l => ActiveStatuses.Contains(l.Status) || l.DocumentsFlaggedAt != null)
                    .OrderBy(l => l.LastActionDate)
                    .Take(200)                                  // bounded work per tick
                    .Select(l => new { l.Id, l.LoanNo, l.DocumentsCompleteAt, l.Status, l.DocumentsFlaggedAt })
                    .ToListAsync(ct);

                using var gate = new SemaphoreSlim(4);        // document-server politeness
                var changes = new List<(int Id, DateTime? Stamp)>();
                var flagSyncs = new List<int>();               // flagged loans to sync

                await Parallel.ForEachAsync(loans, ct, async (loan, token) =>
                {
                    await gate.WaitAsync(token);
                    try
                    {
                        var result = await completeness.CheckByLoanNoAsync(loan.LoanNo, token);
                        // Keep the original verification time when already stamped
                        // (avoids timestamp churn); null when incomplete.
                        DateTime? stamp = result.Complete
                            ? loan.DocumentsCompleteAt ?? time.UtcNow
                            : null;
                        if (stamp != loan.DocumentsCompleteAt)
                            lock (changes) changes.Add((loan.Id, stamp));

                        // Flag sync: flagged loans that flip complete
                        if (result.Complete && loan.DocumentsFlaggedAt != null)
                            lock (flagSyncs) flagSyncs.Add(loan.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Completeness sweep failed for loan {Id}.", loan.Id);
                    }
                    finally { gate.Release(); }
                });

                if (changes.Count > 0)
                {
                    foreach (var (id, stamp) in changes)
                    {
                        var row = await db.LoanApplications.FindAsync([id], ct);
                        if (row is not null) row.DocumentsCompleteAt = stamp;
                    }
                    await db.SaveChangesAsync(ct);
                    _logger.LogInformation("Completeness sweep reconciled {Count} loan(s).", changes.Count);
                }

                // Sync document flags: clear the flag when every requirement
                // verifies complete on the document server.
                if (flagSyncs.Count > 0)
                {
                    var documentFlag = scope.ServiceProvider.GetRequiredService<IDocumentGateService>();
                    foreach (var id in flagSyncs)
                    {
                        var row = await db.LoanApplications.FindAsync([id], ct);
                        if (row is null) continue;

                        var cleared = await documentFlag.SyncAsync(row, ct);
                        if (cleared)
                            _logger.LogInformation(
                                "Auto-cleared document flag on loan {LamId}: all requirements uploaded.",
                                row.LamId);
                        else
                            _logger.LogWarning(
                                "Flag sync skipped for loan {Id}: still incomplete or not flagged.", id);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Completeness sweep cycle failed; retrying next interval.");
            }

            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}

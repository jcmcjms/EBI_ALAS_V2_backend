using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using EBI.ALAS.Api.Common.Constants;
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
/// Also auto-returns parked loans (ForIncompleteDocuments) to the Checking
/// queue once every requirement verifies complete on the document server.
/// Uses the System actor (single workflow edge) so the transition is audited.
/// </summary>
public sealed class DocumentCompletenessSyncHostedService : BackgroundService
{
    private static readonly string[] ActiveStatuses =
        ["ForRecommendation", "ForChecking", "ForApproval", "ForRevision", "ForIncompleteDocuments"];

    /// <summary>System actor for auto-return transitions. Has exactly one
    /// workflow edge: ForIncompleteDocuments → ForChecking.</summary>
    private static readonly ClaimsPrincipal SystemPrincipal = new(
        new ClaimsIdentity([
            new Claim(ClaimTypes.Role, Roles.System),
            new Claim("userId", "0"),
            new Claim(ClaimTypes.Name, Roles.DisplayNames.System),
        ]));

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
                    .Where(l => ActiveStatuses.Contains(l.Status))
                    .OrderBy(l => l.LastActionDate)
                    .Take(200)                                  // bounded work per tick
                    .Select(l => new { l.Id, l.LoanNo, l.DocumentsCompleteAt, l.Status })
                    .ToListAsync(ct);

                using var gate = new SemaphoreSlim(4);        // document-server politeness
                var changes = new List<(int Id, DateTime? Stamp)>();
                var returns = new List<int>();                // ForIncompleteDocuments → ForChecking

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

                        // Auto-return: parked loans that flip complete
                        if (result.Complete && loan.Status == "ForIncompleteDocuments")
                            lock (returns) returns.Add(loan.Id);
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

                // Auto-return parked loans to the Checking queue via the
                // shared transition pipeline (System actor, audited).
                if (returns.Count > 0)
                {
                    var transition = scope.ServiceProvider.GetRequiredService<ILoanStatusTransitionService>();
                    foreach (var id in returns)
                    {
                        var result = await transition.TryTransitionAsync(
                            id,
                            targetStatus: "ForChecking",
                            comments: "All document requirements verified complete on the document server — returned to the Checking queue.",
                            verdict: null,
                            missingCodes: null,
                            submittedCodes: null,
                            user: SystemPrincipal,
                            ct);

                        if (result.Error is not null)
                            _logger.LogWarning(
                                "Auto-return failed for loan {LamId}: {Error}",
                                result.LamId, result.Error);
                        else
                            _logger.LogInformation(
                                "Auto-returned loan {LamId} from ForIncompleteDocuments to ForChecking.",
                                result.LamId);
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

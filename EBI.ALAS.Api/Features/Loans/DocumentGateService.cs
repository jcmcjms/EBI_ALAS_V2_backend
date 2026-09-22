using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Resolves the seeded "system" user so audit rows written by automated
/// gate actions (sweep, auto-hold) attribute to a real principal instead
/// of borrowing a human's id.
/// </summary>
public interface ISystemPrincipal
{
    Task<int> GetIdAsync(CancellationToken ct);
}

public sealed class SystemPrincipal(AppDbContext db, IMemoryCache cache) : ISystemPrincipal
{
    public async Task<int> GetIdAsync(CancellationToken ct)
    {
        if (cache.TryGetValue<int>("system:userId", out var id))
            return id;

        id = await db.Users.AsNoTracking()
            .Where(u => u.Username == "system")
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);

        if (id == 0)
            throw new InvalidOperationException(
                "System user not found. Ensure DbInitializer has seeded a user with Username == 'system'.");

        cache.Set("system:userId", id, TimeSpan.FromHours(1));
        return id;
    }
}

/// <summary>
/// Owns the ForIncompleteDocuments lifecycle so queue membership is ALWAYS derived
/// from the document server, never from a human remembering to click:
///   entry  — submission or any promotion into a review desk,
///   exit   — completeness sweep, or an on-demand /documents/verify recheck.
///
/// Mid-review regressions (stamp flips to null while a reviewer holds the file)
/// deliberately do NOT auto-hold: yanking a file mid-evaluation is worse than
/// letting the reviewer use the manual override with remarks.
/// </summary>
public interface IDocumentGateService
{
    /// <summary>
    /// Entry gate: if the document server reports missing requirements,
    /// hold the loan in ForIncompleteDocuments instead of the intended review desk.
    /// Returns true when the loan was held.
    /// </summary>
    Task<bool> HoldIfIncompleteAsync(LoanApplication loan, string intendedStatus, int actorUserId, CancellationToken ct);

    /// <summary>
    /// Exit gate: when a held loan becomes document-complete, return it to
    /// the desk it was held from and re-queue it. Returns true when released.
    /// </summary>
    Task<bool> ReleaseIfCompleteAsync(LoanApplication loan, int? actorUserId, CancellationToken ct);
}

public sealed class DocumentGateService(
    AppDbContext db,
    IDocumentCompletenessService completeness,
    IDocumentChecklistStore checklistStore,
    IWorkflowQueueService queueService,
    IAuditLogger auditLogger,
    ITimeProvider timeProvider,
    INotificationService notifications,
    IRealtimeNotificationService realtime,
    ISystemPrincipal system) : IDocumentGateService
{
    private static readonly string[] ReviewStatuses =
        ["ForRecommendation", "ForChecking", "ForApproval"];

    public async Task<bool> HoldIfIncompleteAsync(
        LoanApplication loan, string intendedStatus, int actorUserId, CancellationToken ct)
    {
        var items = await completeness.GetItemsByLoanNoAsync(loan.LoanNo, ct);
        var missing = items.Where(i => i.UploadStatus != "Uploaded").Select(i => i.IdCode).ToList();

        if (missing.Count == 0)
        {
            loan.DocumentsCompleteAt ??= timeProvider.UtcNow;
            return false;
        }

        // Human-readable labels: "Name (Code)" so auditors see both.
        var missingLabels = items
            .Where(i => i.UploadStatus != "Uploaded")
            .Select(i => $"{i.ChecklistDescription ?? i.IdCode} ({i.IdCode})")
            .ToList();

        var from = loan.Status;

        // Record missing items first so the checklist is populated before
        // the status change is persisted (avoids a held loan with no checklist).
        await checklistStore.MarkMissingAsync(loan.Id, missing, actorUserId, ct);

        loan.Status = "ForIncompleteDocuments";
        loan.IncompleteReturnStatus = intendedStatus;
        loan.DocumentsCompleteAt = null;
        loan.LastActionDate = timeProvider.UtcNow;
        await db.SaveChangesAsync(ct);

        // Desk lifecycle: leave the review desk, enter the document desk.
        if (WorkflowQueueService.StageForStatus(from) != null)
            await queueService.DequeueAndPromoteAsync(loan, from, ct);
        await queueService.EnqueueAsync(loan, "ForIncompleteDocuments", ct);

        await auditLogger.LogActionAsync(loan.Id, actorUserId, "StatusChanged", from,
            "ForIncompleteDocuments",
            $"Auto-held on entry to {intendedStatus} — missing: {string.Join(", ", missingLabels)}");

        var link = $"/loans/monitoring?id={loan.Id}";
        var title = "Documents Incomplete — Action Required";
        var body = $"{loan.LamId} was placed in the Incomplete Documents queue automatically. " +
                   $"Missing: {string.Join(", ", missingLabels)}. It returns to {intendedStatus} once uploaded.";
        await notifications.CreateAsync(loan.CreatedById, title, body, link);
        await realtime.NotifyUserAsync(loan.CreatedById, title, body, link);
        await realtime.NotifyDashboardUpdateAsync(loan.BranchCode);
        return true;
    }

    public async Task<bool> ReleaseIfCompleteAsync(
        LoanApplication loan, int? actorUserId, CancellationToken ct)
    {
        if (loan.Status != "ForIncompleteDocuments")
            return false;

        var result = await completeness.CheckByLoanNoAsync(loan.LoanNo, ct);
        if (!result.Complete)
            return false;

        var target = ReviewStatuses.Contains(loan.IncompleteReturnStatus)
            ? loan.IncompleteReturnStatus!
            : "ForChecking"; // legacy rows predating the column

        var actor = actorUserId ?? await system.GetIdAsync(ct);
        var from = loan.Status;

        loan.Status = target;
        loan.IncompleteReturnStatus = null;
        loan.DocumentsCompleteAt = timeProvider.UtcNow;
        loan.LastActionDate = timeProvider.UtcNow;
        await db.SaveChangesAsync(ct);

        await queueService.DequeueAndPromoteAsync(loan, from, ct);
        await queueService.EnqueueAsync(loan, target, ct);

        await auditLogger.LogActionAsync(loan.Id, actor, "StatusChanged", from, target,
            "Auto-released: all document requirements uploaded.");

        var link = $"/loans/monitoring?id={loan.Id}";
        var title = "Documents Complete — Back in Queue";
        var body = $"{loan.LamId} has all requirements uploaded and returned to {target}.";
        await notifications.CreateAsync(loan.CreatedById, title, body, link);
        await realtime.NotifyUserAsync(loan.CreatedById, title, body, link);

        // Wake the destination desk (recommenders / evaluators / approvers).
        var deskRole = target switch
        {
            "ForRecommendation" => Roles.Recommender,
            "ForApproval" => Roles.Approver,
            _ => Roles.Evaluator,
        };
        foreach (var reviewer in await db.Users.AsNoTracking()
                     .Where(u => u.Role == deskRole && u.BranchId == loan.BranchCode && u.IsActive)
                     .ToListAsync(ct))
        {
            await notifications.CreateAsync(reviewer.Id, title, body, link);
            await realtime.NotifyUserAsync(reviewer.Id, title, body, link);
        }

        await realtime.NotifyDashboardUpdateAsync(loan.BranchCode);
        return true;
    }
}

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
/// Owns the ForIncompleteDocuments lifecycle:
///   entry  — a reviewer explicitly flags a file with the requirements they found
///            lacking plus a written reason (this is the ONLY entry path).
///   exit   — completeness sweep auto-releases when every requirement verifies
///            complete on the document server.
///
/// Submission and desk promotions never hold automatically — files go straight
/// to the review desk regardless of document completeness.
/// </summary>
public interface IDocumentGateService
{
    /// <summary>
    /// The ONLY entry path into ForIncompleteDocuments: a reviewer explicitly
    /// flags the file with the requirements they found lacking plus a written
    /// reason. Submission and desk promotions never hold automatically.
    /// </summary>
    Task FlagIncompleteAsync(
        LoanApplication loan,
        IReadOnlyCollection<string> missingCodes,
        string comment,
        int actorUserId,
        CancellationToken ct);

    /// <summary>
    /// Exit gate: when a held loan becomes document-complete, return it to
    /// the desk it was flagged from and re-queue it. Returns true when released.
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

    public async Task FlagIncompleteAsync(
        LoanApplication loan, IReadOnlyCollection<string> missingCodes, string comment,
        int actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(loan);
        if (missingCodes.Count == 0)
            throw new ArgumentException("At least one missing requirement is required to flag a file.", nameof(missingCodes));

        // Labels for audit/notification: "Name (Code)" so encoders and auditors
        // never cross-reference the checklist by code alone.
        var items = await completeness.GetItemsByLoanNoAsync(loan.LoanNo, ct);
        var missingLabels = items
            .Where(i => missingCodes.Contains(i.IdCode))
            .Select(i => $"{i.ChecklistDescription ?? i.IdCode} ({i.IdCode})")
            .ToList();

        var from = loan.Status;

        await checklistStore.MarkMissingAsync(loan.Id, missingCodes.ToList(), actorUserId, ct);

        loan.Status = "ForIncompleteDocuments";
        loan.IncompleteReturnStatus = from;      // auto-release returns to the flagging desk
        loan.DocumentsCompleteAt = null;
        loan.LastActionDate = timeProvider.UtcNow;
        await db.SaveChangesAsync(ct);

        if (WorkflowQueueService.StageForStatus(from) != null)
            await queueService.DequeueAndPromoteAsync(loan, from, ct);
        await queueService.EnqueueAsync(loan, "ForIncompleteDocuments", ct);

        await auditLogger.LogActionAsync(loan.Id, actorUserId, "StatusChanged", from,
            "ForIncompleteDocuments",
            $"Flagged as lacking documents — missing: {string.Join(", ", missingLabels)}. Reason: {comment}");

        var link = $"/loans/monitoring?id={loan.Id}";
        var title = "Documents Incomplete — Action Required";
        var body = $"A reviewer flagged {loan.LamId} as lacking documents. " +
                   $"Missing: {string.Join(", ", missingLabels)}. Reason: {comment} " +
                   $"It returns to {from} once every requirement verifies complete.";
        await notifications.CreateAsync(loan.CreatedById, title, body, link);
        await realtime.NotifyUserAsync(loan.CreatedById, title, body, link);
        await realtime.NotifyDashboardUpdateAsync(loan.BranchCode);
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

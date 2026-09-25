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
/// flag actions (sync sweep) attribute to a real principal instead
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
/// Owns the document flag lifecycle — a deficiency is a FACT about paperwork,
/// NOT a routing decision. Flag columns + checklist state record the deficiency
/// without touching Status or workflow queues.
///
///   FlagAsync  — a reviewer explicitly flags a file with the requirements they
///                found lacking plus a written reason. The ONLY entry path.
///   ClearAsync — manual withdraw (flagger/admin) or encoder-side clear.
///   SyncAsync  — auto-clear when every requirement verifies complete on the
///                document server (called by hosted service + verify endpoint).
///
/// The status never changes. The file never leaves its desk. The approver
/// routing path is the same code whether or not documents are missing.
/// </summary>
public interface IDocumentGateService
{
    /// <summary>
    /// Records a document deficiency WITHOUT touching Status or queues.
    /// Sets flag columns, marks checklist items, logs audit, sends notifications.
    /// </summary>
    Task FlagAsync(
        LoanApplication loan,
        IReadOnlyCollection<string> missingCodes,
        string reason,
        int actorUserId,
        CancellationToken ct);

    /// <summary>
    /// Manual withdraw: clears the flag columns and logs the clear action.
    /// Available to the flagger, admin, or encoder after uploading.
    /// Returns true when a flag was actually cleared.
    /// </summary>
    Task<bool> ClearAsync(
        LoanApplication loan,
        int? actorUserId,
        string cause,
        CancellationToken ct);

    /// <summary>
    /// Sync hook: checks document completeness and clears the flag if everything
    /// is now uploaded. Called by DocumentCompletenessSyncHostedService and the
    /// POST /api/loans/{id}/documents/verify endpoint.
    /// Returns true when a flag was cleared.
    /// </summary>
    Task<bool> SyncAsync(LoanApplication loan, CancellationToken ct);
}

public sealed class DocumentFlagService(
    AppDbContext db,
    IDocumentCompletenessService completeness,
    IDocumentChecklistStore checklistStore,
    IAuditLogger auditLogger,
    ITimeProvider timeProvider,
    INotificationService notifications,
    IRealtimeNotificationService realtime,
    ISystemPrincipal system) : IDocumentGateService
{
    public async Task FlagAsync(
        LoanApplication loan, IReadOnlyCollection<string> missingCodes, string reason,
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

        // Mark checklist items as missing
        await checklistStore.MarkMissingAsync(loan.Id, missingCodes.ToList(), actorUserId, ct);

        // Set flag columns — no status change, no queue mutation
        loan.DocumentsFlaggedAt = timeProvider.UtcNow;
        loan.DocumentsFlaggedById = actorUserId;
        loan.DocumentFlagReason = reason;
        loan.DocumentsCompleteAt = null;
        await db.SaveChangesAsync(ct);

        // Audit: dedicated verb (not a status transition)
        await auditLogger.LogActionAsync(loan.Id, actorUserId, "DocumentsFlagged", null, null,
            $"Missing: {string.Join(", ", missingLabels)}. Reason: {reason}");

        // Notify encoder: documents flagged — upload required
        var link = $"/loans/monitoring?id={loan.Id}";
        var title = "Documents Flagged — Upload Required";
        var body = $"A reviewer flagged {loan.LamId} as lacking documents. " +
                   $"Missing: {string.Join(", ", missingLabels)}. Reason: {reason}. " +
                   $"Upload the missing documents; the flag clears automatically when complete.";
        await notifications.CreateAsync(loan.CreatedById, title, body, link);
        await realtime.NotifyUserAsync(loan.CreatedById, title, body, link);
        await realtime.NotifyDashboardUpdateAsync(loan.BranchCode);
    }

    public async Task<bool> ClearAsync(
        LoanApplication loan, int? actorUserId, string cause, CancellationToken ct)
    {
        if (loan.DocumentsFlaggedAt is null)
            return false;

        var actor = actorUserId ?? await system.GetIdAsync(ct);

        // Clear flag columns
        loan.DocumentsFlaggedAt = null;
        loan.DocumentsFlaggedById = null;
        loan.DocumentFlagReason = null;
        await db.SaveChangesAsync(ct);

        // Audit: dedicated verb
        await auditLogger.LogActionAsync(loan.Id, actor, "DocumentFlagCleared", null, null, cause);

        // Notify encoder: flag cleared
        var link = $"/loans/monitoring?id={loan.Id}";
        var title = "Document Flag Cleared";
        var body = $"The document flag on {loan.LamId} has been cleared. {cause}";
        await notifications.CreateAsync(loan.CreatedById, title, body, link);
        await realtime.NotifyUserAsync(loan.CreatedById, title, body, link);

        // Notify the flagger (if different from actor and encoder)
        if (loan.DocumentsFlaggedById.HasValue
            && loan.DocumentsFlaggedById != actorUserId
            && loan.DocumentsFlaggedById != loan.CreatedById)
        {
            await notifications.CreateAsync(loan.DocumentsFlaggedById.Value, title, body, link);
            await realtime.NotifyUserAsync(loan.DocumentsFlaggedById.Value, title, body, link);
        }

        await realtime.NotifyDashboardUpdateAsync(loan.BranchCode);
        return true;
    }

    public async Task<bool> SyncAsync(LoanApplication loan, CancellationToken ct)
    {
        if (loan.DocumentsFlaggedAt is null)
            return false;

        var result = await completeness.CheckByLoanNoAsync(loan.LoanNo, ct);
        if (!result.Complete)
            return false;

        return await ClearAsync(loan, null, "Auto-cleared: all flagged requirements uploaded.", ct);
    }
}

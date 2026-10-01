using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
namespace EBI.ALAS.Api.Features.Loans;
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
public interface IDocumentGateService
{
    Task FlagAsync(
        LoanApplication loan,
        IReadOnlyCollection<string> missingCodes,
        string reason,
        int actorUserId,
        CancellationToken ct);
    Task<bool> ClearAsync(
        LoanApplication loan,
        int? actorUserId,
        string cause,
        CancellationToken ct);
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
        var items = await completeness.GetItemsByLoanNoAsync(loan.LoanNo, ct);
        var missingLabels = items
            .Where(i => missingCodes.Contains(i.IdCode))
            .Select(i => $"{i.ChecklistDescription ?? i.IdCode} ({i.IdCode})")
            .ToList();
        await checklistStore.MarkMissingAsync(loan.Id, missingCodes.ToList(), actorUserId, ct);
        loan.DocumentsFlaggedAt = timeProvider.UtcNow;
        loan.DocumentsFlaggedById = actorUserId;
        loan.DocumentFlagReason = reason;
        loan.DocumentsCompleteAt = null;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogActionAsync(loan.Id, actorUserId, "DocumentsFlagged", null, null,
            $"Missing: {string.Join(", ", missingLabels)}. Reason: {reason}");
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
        loan.DocumentsFlaggedAt = null;
        loan.DocumentsFlaggedById = null;
        loan.DocumentFlagReason = null;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogActionAsync(loan.Id, actor, "DocumentFlagCleared", null, null, cause);
        var link = $"/loans/monitoring?id={loan.Id}";
        var title = "Document Flag Cleared";
        var body = $"The document flag on {loan.LamId} has been cleared. {cause}";
        await notifications.CreateAsync(loan.CreatedById, title, body, link);
        await realtime.NotifyUserAsync(loan.CreatedById, title, body, link);
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

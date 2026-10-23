using Ebi.Alas.Api.Features.Branches;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Notifications;

public sealed class ApplicationArrivalNotifier(
    AlasDbContext db,
    IRealtimeNotifier? notifier,
    TimeProvider timeProvider)
{
    public async Task NotifyRoleAsync(
        UserRole role,
        string branchId,
        string title,
        string body,
        string? link,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var query = db.Users.AsNoTracking()
            .Where(u => u.Status == UserStatus.Active && u.Role == role);
        if (role is UserRole.Evaluator or UserRole.Approver)
        {
            query = query.Where(u => u.IsAvailable);
        }

        var candidates = await query
            .Select(u => new { u.Id, u.BranchId, u.JobTitle })
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return;
        }

        var homeCodes = candidates.Select(c => c.BranchId).Distinct().ToList();
        var areaByHome = await db.Branches.AsNoTracking()
            .Where(b => homeCodes.Contains(b.Code))
            .ToDictionaryAsync(b => b.Code, b => b.AreaCode, cancellationToken);
        var areaCodes = areaByHome.Values
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a!)
            .Distinct()
            .ToList();
        var areaBranchRows = areaCodes.Count == 0
            ? []
            : await db.Branches.AsNoTracking()
                .Where(b => areaCodes.Contains(b.AreaCode))
                .Select(b => new { b.Code, b.AreaCode })
                .ToListAsync(cancellationToken);
        var branchesByArea = areaBranchRows
            .GroupBy(b => b.AreaCode)
            .ToDictionary(g => g.Key, g => g.Select(b => b.Code).ToList());

        var scopeResolution = await new Approvals.ApprovalScopeService(db).ResolveAsync(
            candidates.Select(c => c.Id).ToList(), cancellationToken);

        var recipientIds = new List<Guid>();
        foreach (var candidate in candidates)
        {
            var home = candidate.BranchId?.Trim() ?? string.Empty;
            var inScope = home == branchId;
            var isAreaScoped = scopeResolution.CatalogConfigured
                ? scopeResolution.ScopeFor(candidate.Id).ScopeType == Approvals.AuthorityScopes.Area
                : BranchVisibility.IsAreaHead(candidate.JobTitle);
            if (!inScope && isAreaScoped)
            {
                if (areaByHome.TryGetValue(home, out var area)
                    && !string.IsNullOrWhiteSpace(area)
                    && branchesByArea.TryGetValue(area!, out var areaBranches))
                {
                    inScope = areaBranches.Contains(branchId);
                }
            }

            if (inScope)
            {
                recipientIds.Add(candidate.Id);
            }
        }

        if (recipientIds.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var notifications = recipientIds
            .Select(userId => Notification.Create(userId, title, body, NotificationType.Application, now, link))
            .ToList();

        db.Notifications.AddRange(notifications);
        await db.SaveChangesAsync(cancellationToken);

        if (notifier is null)
        {
            return;
        }

        foreach (var notification in notifications)
        {
            await notifier.NotifyUserAsync(
                notification.Id,
                notification.UserId,
                notification.Title,
                notification.Body,
                link,
                notification.CreatedAt,
                cancellationToken);
        }
    }
}

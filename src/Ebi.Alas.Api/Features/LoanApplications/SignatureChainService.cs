using Ebi.Alas.Api.Features.Approvals;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications;

/// <summary>
/// Printable approval form always shows Recommend / Evaluate / Approve slots.
/// Loan chain rows are created at submit and signed on workflow actions.
/// </summary>
public static class SignatureChainService
{
    public sealed record SignatureSlotDisplay(
        int Order,
        string Action,
        string Role,
        string JobTitle,
        string? SignedByName,
        string? SignedByJobTitle,
        DateTimeOffset? SignedAt);

    private static readonly (int Order, string Action, string Role, string JobTitle)[] Template =
    [
        (1, "Recommend", "Recommender", "Branch Head"),
        (2, "Evaluate", "Evaluator", "Credit Checker"),
        (3, "Approve", "Approver", "Area Head"),
    ];

    public static async Task EnsureCreatedAsync(
        AlasDbContext db,
        Guid loanApplicationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var exists = await db.SignatureChainEntries.AsNoTracking()
            .AnyAsync(s => s.LoanApplicationId == loanApplicationId, cancellationToken);
        if (exists)
        {
            return;
        }

        foreach (var slot in Template)
        {
            db.SignatureChainEntries.Add(SignatureChainEntry.Create(
                loanApplicationId, slot.Order, slot.Role, now));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task SignAsync(
        AlasDbContext db,
        Guid loanApplicationId,
        string roleName,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var entry = await db.SignatureChainEntries
            .FirstOrDefaultAsync(
                s => s.LoanApplicationId == loanApplicationId
                    && s.RoleName == roleName,
                cancellationToken);
        if (entry is null)
        {
            return;
        }

        entry.Sign(userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task<IReadOnlyList<SignatureSlotDisplay>> GetDisplayChainAsync(
        AlasDbContext db,
        Guid loanApplicationId,
        CancellationToken cancellationToken)
    {
        var entries = await db.SignatureChainEntries.AsNoTracking()
            .Where(s => s.LoanApplicationId == loanApplicationId)
            .ToListAsync(cancellationToken);

        var userIds = entries
            .Where(e => e.UserId != null)
            .Select(e => e.UserId!.Value)
            .Distinct()
            .ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName, u.JobTitle })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var result = new List<SignatureSlotDisplay>(Template.Length);
        foreach (var slot in Template)
        {
            var entry = entries.FirstOrDefault(e =>
                string.Equals(e.RoleName, slot.Role, StringComparison.OrdinalIgnoreCase)
                || e.Order == slot.Order);
            string? signedByName = null;
            string? signedByJobTitle = null;
            if (entry?.UserId is { } userId && users.TryGetValue(userId, out var user))
            {
                signedByName = user.Name;
                signedByJobTitle = user.JobTitle;
            }

            result.Add(new SignatureSlotDisplay(
                slot.Order,
                slot.Action,
                slot.Role,
                slot.JobTitle,
                signedByName,
                signedByJobTitle,
                entry?.SignedAt));
        }

        return result;
    }
}

using Ebi.Alas.Api.Features.Approvals;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications;

/// <summary>
/// Signature slots follow workflow: Encoder is always required; Recommender
/// only when recommendation is enabled before evaluation. Evaluate/Approve always apply.
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
        DateTimeOffset? SignedAt,
        string? SignatureUrl);

    private sealed record SlotTemplate(int Order, string Action, string Role, string JobTitle);

    private static readonly SlotTemplate EncoderSlot = new(1, "Encode", "Encoder", "Account Officer");
    private static readonly SlotTemplate RecommenderSlot = new(2, "Recommend", "Recommender", "Branch Head");
    private static readonly SlotTemplate EvaluatorSlot = new(3, "Evaluate", "Evaluator", "Credit Checker");
    private static readonly SlotTemplate ApproverSlot = new(4, "Approve", "Approver", "Area Head");

    private static readonly SlotTemplate EvaluatorSlotNoRecommend = new(2, "Evaluate", "Evaluator", "Credit Checker");
    private static readonly SlotTemplate ApproverSlotNoRecommend = new(3, "Approve", "Approver", "Area Head");

    private static IReadOnlyList<SlotTemplate> BuildTemplate(bool requireRecommendation) =>
        requireRecommendation
            ? [EncoderSlot, RecommenderSlot, EvaluatorSlot, ApproverSlot]
            : [EncoderSlot, EvaluatorSlotNoRecommend, ApproverSlotNoRecommend];

    public static async Task EnsureCreatedAsync(
        AlasDbContext db,
        Guid loanApplicationId,
        bool requireRecommendation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var exists = await db.SignatureChainEntries.AsNoTracking()
            .AnyAsync(s => s.LoanApplicationId == loanApplicationId, cancellationToken);
        if (exists)
        {
            return;
        }

        foreach (var slot in BuildTemplate(requireRecommendation))
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

        // Snapshot the signer's ink at the moment of signing so the loan record
        // preserves exactly what was signed, even if the profile signature changes later.
        var ink = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.ESignature)
            .FirstOrDefaultAsync(cancellationToken);

        var snapshot = Users.Domain.ESignaturePolicy.SanitizeForDisplay(ink);
        if (snapshot is null || !Users.Domain.ESignaturePolicy.IsEligibleForSigning(ink))
        {
            throw new Composition.Errors.ValidationException(
                "A saved PNG e-signature is required before completing this workflow action. Upload one in your account profile.");
        }

        entry.Sign(userId, now, snapshot);
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task<IReadOnlyList<SignatureSlotDisplay>> GetDisplayChainAsync(
        AlasDbContext db,
        Guid loanApplicationId,
        bool requireRecommendation,
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

        var template = BuildTemplate(requireRecommendation);
        var result = new List<SignatureSlotDisplay>(template.Count);
        foreach (var slot in template)
        {
            var entry = entries.FirstOrDefault(e =>
                string.Equals(e.RoleName, slot.Role, StringComparison.OrdinalIgnoreCase));
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
                entry?.SignedAt,
                SignatureUrl: entry is null
                    ? null
                    : Users.Domain.ESignaturePolicy.Sanitize(entry.SignatureImage)));
        }

        return result;
    }
}

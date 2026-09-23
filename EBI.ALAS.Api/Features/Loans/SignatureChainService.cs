using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Resolves the signature chain from the append-only LoanActions audit trail
/// and the hot-reloadable IWorkflowConfiguration.RequireRecommendation flag.
///
/// Capacity titles mirror the role table in the system docs; kept here so the
/// printed form never depends on role display-name edits.
/// </summary>
public sealed class SignatureChainService(AppDbContext context, IWorkflowConfiguration config)
    : ISignatureChainService
{
    private const string EncoderTitle = "Account Officer / Credit Analyst Assistant";
    private const string RecommenderTitle = "Branch Head";
    private const string EvaluatorTitle = "Credit Analyst / Credit Checker";
    private const string ApproverTitle = "Area Head";

    public IReadOnlyList<SignatureSlotDto> GetTemplate() =>
        Build(config.RequireRecommendation, signers: []);

    public async Task<IReadOnlyList<SignatureSlotDto>?> ResolveForLoanAsync(
        int loanApplicationId, CancellationToken ct = default)
    {
        var exists = await context.LoanApplications
            .AsNoTracking()
            .AnyAsync(l => l.Id == loanApplicationId, ct);
        if (!exists) return null;

        // Append-only audit trail is the single source of truth for who signed
        // what and when — never re-derive from status columns.
        var actions = await context.LoanActions
            .AsNoTracking()
            .Where(a => a.LoanApplicationId == loanApplicationId)
            .Include(a => a.ActionByUser)
            .OrderBy(a => a.ActionDate)
            .ToListAsync(ct);

        Signer? prepared = SignerOf(actions, a => a.Action == "Created");
        Signer? recommended = SignerOf(actions,
            a => a.FromStatus == "ForRecommendation" && a.ToStatus == "ForChecking");
        Signer? checked_ = SignerOf(actions,
            a => a.FromStatus == "ForChecking" && a.ToStatus == "ForApproval");
        Signer? approved = SignerOf(actions,
            a => a.FromStatus == "ForApproval" && a.ToStatus == "Approved");

        var signers = new Dictionary<string, Signer?>
        {
            [Roles.Encoder] = prepared,
            [Roles.Recommender] = recommended,
            [Roles.Evaluator] = checked_,
            [Roles.Approver] = approved,
        };

        return Build(config.RequireRecommendation, signers);
    }

    private static Signer? SignerOf(List<LoanAction> actions, Func<LoanAction, bool> match)
    {
        var hit = actions.FirstOrDefault(match);
        return hit is null ? null : new Signer(
            DisplayName(hit.ActionByUser), hit.ActionByUser.JobTitle, hit.ActionDate);
    }

    private static string DisplayName(User? user)
    {
        if (user is null) return string.Empty;
        var parts = new[] { user.FirstName, user.MiddleName, user.LastName }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(" ", parts);
    }

    private static List<SignatureSlotDto> Build(
        bool requireRecommendation, Dictionary<string, Signer?> signers)
    {
        var slots = new List<(string Action, string Role, string Title)>
        {
            ("Prepared by", Roles.Encoder, EncoderTitle),
        };
        if (requireRecommendation)
            slots.Add(("Recommended by", Roles.Recommender, RecommenderTitle));
        slots.Add(("Checked by", Roles.Evaluator, EvaluatorTitle));
        slots.Add(("Approved by", Roles.Approver, ApproverTitle));

        return slots
            .Select((s, i) =>
            {
                signers.TryGetValue(s.Role, out var signer);
                return new SignatureSlotDto(
                    i + 1,
                    s.Action,
                    s.Role,
                    s.Title,
                    signer?.Name,
                    signer?.JobTitle,
                    signer?.SignedAt);
            })
            .ToList();
    }

    private sealed record Signer(string Name, string? JobTitle, DateTime SignedAt);
}

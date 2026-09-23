namespace EBI.ALAS.Api.Features.Loans;

/// <summary>One signature line on page 2 of the Loan Approval Form.</summary>
public sealed record SignatureSlotDto(
    int Order,
    /// <summary>"Prepared by" / "Recommended by" / "Checked by" / "Approved by".</summary>
    string Action,
    /// <summary>Workflow role that owns this slot (Roles.Encoder, etc.).</summary>
    string Role,
    /// <summary>Capacity title printed when the slot is blank (e.g. "Branch Head").</summary>
    string JobTitle,
    /// <summary>Null when the slot has not been signed yet.</summary>
    string? SignedByName,
    /// <summary>Signer's job title at the time of signing (from User.JobTitle).</summary>
    string? SignedByJobTitle,
    /// <summary>UTC timestamp of the signing action (from LoanAction.ActionDate).</summary>
    DateTime? SignedAt);

/// <summary>
/// Resolves the signature chain for the Loan Approval Form page 2.
/// Template mode returns unsigned slots for draft preview; resolved mode
/// populates signers from the append-only LoanActions audit trail.
/// </summary>
public interface ISignatureChainService
{
    /// <summary>Unsigned template (draft preview on the create page).</summary>
    IReadOnlyList<SignatureSlotDto> GetTemplate();

    /// <summary>Chain with signers resolved from LoanActions; null when the loan does not exist.</summary>
    Task<IReadOnlyList<SignatureSlotDto>?> ResolveForLoanAsync(int loanApplicationId, CancellationToken ct = default);
}

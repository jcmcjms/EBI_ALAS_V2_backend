using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Per-item document requirement status for a loan application.
/// Tracks the lifecycle of each checklist requirement from the evaluator's
/// "missing" flag through the encoder's "submitted" to eventual verification.
///
/// Keyed by (LoanApplicationId, Code) where Code is the stable BPB checklist
/// requirement code (id_code), matching DocumentRemark.ChecklistIdCode.
/// </summary>
public class DocumentChecklist
{
    public int Id { get; set; }

    public int LoanApplicationId { get; set; }
    public LoanApplication LoanApplication { get; set; } = null!;

    /// <summary>BPB checklist requirement code (id_code), e.g. "PAYSLIP", "CIBI".</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable requirement name (e.g. "Latest payslip").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Current status: "Missing" | "Pending" | "Submitted" | "Verified".
    /// - Missing: evaluator flagged this as required
    /// - Pending: default state from product checklist (not yet reviewed)
    /// - Submitted: encoder marked as uploaded
    /// - Verified: evaluator confirmed completeness
    /// </summary>
    public string Status { get; set; } = "Pending";

    /// <summary>doc_ref snapshot when the document was last uploaded/verified.</summary>
    public int? DocId { get; set; }

    /// <summary>Last update timestamp (UTC).</summary>
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>User who last updated this item.</summary>
    public int? UpdatedById { get; set; }
    public User? UpdatedBy { get; set; }
}

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// One declared deviation, normalized from the submission's JSON snapshot at
/// write time so the remark conversation has a stable integer key. The JSON
/// columns on LoanApplication remain the immutable print/audit snapshot.
/// </summary>
public class LoanDeviation
{
    public int Id { get; set; }
    public int LoanApplicationId { get; set; }
    public LoanApplication LoanApplication { get; set; } = null!;

    /// <summary>Verbatim reason text from DeviationsSection.DeviationDetails.</summary>
    public string ReasonText { get; set; } = string.Empty;

    /// <summary>The encoder's justification captured at submission (thread root).</summary>
    public string EncoderJustification { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    /// <summary>True for the synthetic fee-override thread (not a checkbox reason).</summary>
    public bool IsFeeOverride { get; set; }

    public ICollection<DeviationRemark> Remarks { get; set; } = new List<DeviationRemark>();

    public const string FeeOverrideReason = "Fee override (notarial / doc stamps / insurance)";
}

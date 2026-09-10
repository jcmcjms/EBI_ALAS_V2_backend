using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// One message in a per-deviation discussion thread.
/// The thread ROOT is the encoder's justification captured at submission
/// (LoanApplication.DeviationJustifications / FeeDeviationJustification) — it is
/// projected on read, not stored here. Rows in this table are the conversation:
/// Recommender / Evaluator remarks on a specific deviation, and the encoder's
/// answers to them (the "reply to the encoder's deviation remark" flow).
/// </summary>
public class DeviationRemark
{
    public int Id { get; set; }
    public int LoanApplicationId { get; set; }
    public LoanApplication LoanApplication { get; set; } = null!;

    /// <summary>Thread key: the exact deviation-reason string from
    /// LoanApplication.DeviationDetails, or DeviationRemarkKeys.FeeOverride.</summary>
    public string DeviationKey { get; set; } = string.Empty;

    /// <summary>Null = top-level reply in the thread; otherwise the remark being answered.</summary>
    public int? ParentRemarkId { get; set; }
    public DeviationRemark? ParentRemark { get; set; }

    public int AuthorId { get; set; }
    public User Author { get; set; } = null!;

    /// <summary>Role snapshot at write time so the trail stays readable after role changes.</summary>
    public string AuthorRole { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<DeviationRemark> Replies { get; set; } = new List<DeviationRemark>();
}

public static class DeviationRemarkKeys
{
    public const string FeeOverride = "FEE_OVERRIDE";
}

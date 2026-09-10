using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// One reply in a deviation's discussion thread. The thread root is the
/// encoder's EncoderJustification (projected on read, not stored here).
/// Rows are the conversation: Recommender / Evaluator remarks on that
/// specific deviation, and the encoder's answers to them.
/// </summary>
public class DeviationRemark
{
    public int Id { get; set; }
    public int LoanDeviationId { get; set; }
    public LoanDeviation Deviation { get; set; } = null!;

    /// <summary>Null = top-level reply; otherwise the remark being answered.</summary>
    public int? ParentRemarkId { get; set; }
    public DeviationRemark? ParentRemark { get; set; }

    public int AuthorId { get; set; }
    public User Author { get; set; } = null!;

    /// <summary>Role snapshot at write time so the trail survives role changes.</summary>
    public string AuthorRole { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<DeviationRemark> Replies { get; set; } = new List<DeviationRemark>();
}

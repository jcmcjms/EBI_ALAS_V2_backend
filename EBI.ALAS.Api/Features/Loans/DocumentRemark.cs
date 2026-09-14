using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// A reviewer remark on one Required Document (checklist requirement).
/// Keyed by the stable checklist code, NOT the binary docId — documents get
/// re-uploaded and remarks must survive that. DocId is snapshotted at write
/// time so the audit trail records exactly which binary version was reviewed.
/// </summary>
public class DocumentRemark
{
    public int Id { get; set; }
    public int LoanApplicationId { get; set; }
    public LoanApplication LoanApplication { get; set; } = null!;

    /// <summary>BPB checklist requirement code (id_code) this remark targets.</summary>
    public string ChecklistIdCode { get; set; } = string.Empty;

    /// <summary>doc_ref snapshot at remark time; null when the slot was still pending.</summary>
    public int? DocId { get; set; }

    /// <summary>Null = top-level remark; otherwise the remark being answered.</summary>
    public int? ParentRemarkId { get; set; }
    public DocumentRemark? ParentRemark { get; set; }

    public int AuthorId { get; set; }
    public User Author { get; set; } = null!;

    /// <summary>Role snapshot at write time so history survives role changes.</summary>
    public string AuthorRole { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<DocumentRemark> Replies { get; set; } = new List<DocumentRemark>();
}

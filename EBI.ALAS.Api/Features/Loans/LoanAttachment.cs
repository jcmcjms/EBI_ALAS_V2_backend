using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// A file attached to a loan application (supporting docs, valid IDs, etc.).
/// Bytes live on disk under LoanAttachments:StoragePath; this row is the index.
/// </summary>
public class LoanAttachment
{
    public int Id { get; set; }
    public int LoanApplicationId { get; set; }
    public LoanApplication LoanApplication { get; set; } = null!;

    /// <summary>Original client file name (display only — never used for storage).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>GUID name on disk: prevents traversal, collisions and path info-leaks.</summary>
    public string StoredFileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }

    /// <summary>Optional business category (Supporting Document, Valid ID, Other…).</summary>
    public string? Category { get; set; }

    public int UploadedById { get; set; }
    public User UploadedBy { get; set; } = null!;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// DTO for checklist documents from BPB_BINARY_SERVER.
/// Represents a loan product checklist item with its associated document (if uploaded).
/// </summary>
public sealed record LoanChecklistDocumentDto
{
    /// <summary>Loan number (e.g., "CL20260934640").</summary>
    public string LoanNo { get; init; } = string.Empty;

    /// <summary>Loan product code.</summary>
    public string LoanProduct { get; init; } = string.Empty;

    /// <summary>Checklist item code (id_code from loan_product_checklist).</summary>
    public string IdCode { get; init; } = string.Empty;

    /// <summary>Description of the checklist item from check_list_all.</summary>
    public string? ChecklistDescription { get; init; }

    /// <summary>Document ID from bpb_binary.dbo.doc_ref (null if not uploaded).</summary>
    public int? DocId { get; init; }

    /// <summary>Document string identifier from bpb_binary.dbo.docs.</summary>
    public string? DocStr { get; init; }

    /// <summary>Mini string (thumbnail/preview) from bpb_binary.dbo.docs.</summary>
    public string? MiniStr { get; init; }

    /// <summary>Content type of the document (e.g., "application/pdf").</summary>
    public string? ContentType { get; init; }

    /// <summary>When the document was created/uploaded.</summary>
    public DateTime? Created { get; init; }

    /// <summary>Who uploaded the document.</summary>
    public string? UploadedBy { get; init; }

    /// <summary>Upload status: "Uploaded" or "No uploaded documents".</summary>
    public string UploadStatus { get; init; } = "No uploaded documents";
}

namespace EBI.ALAS.Api.Features.Loans;
public sealed record LoanChecklistDocumentDto
{
    public string LoanNo { get; init; } = string.Empty;
    public string LoanProduct { get; init; } = string.Empty;
    public string IdCode { get; init; } = string.Empty;
    public string? ChecklistDescription { get; init; }
    public int? DocId { get; init; }
    public string? DocStr { get; init; }
    public string? MiniStr { get; init; }
    public string? ContentType { get; init; }
    public DateTime? Created { get; init; }
    public string? UploadedBy { get; init; }
    public string UploadStatus { get; init; } = "No uploaded documents";
}

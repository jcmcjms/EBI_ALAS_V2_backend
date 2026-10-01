namespace EBI.ALAS.Api.Features.Loans;
public interface IChecklistDocumentRepository
{
    Task<List<LoanChecklistDocumentDto>> GetChecklistDocumentsAsync(
        string loanNo,
        CancellationToken cancellationToken = default);
    Task<DocumentContentDto?> GetDocumentContentAsync(
        int docId,
        CancellationToken cancellationToken = default);
    Task<string?> GetDocumentLoanNoAsync(
        int docId,
        CancellationToken cancellationToken = default);
}
public sealed record DocumentContentDto
{
    public byte[] Content { get; init; } = Array.Empty<byte>();
    public string ContentType { get; init; } = "application/octet-stream";
    public string? FileName { get; init; }
}

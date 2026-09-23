namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Repository for querying checklist documents from BPB_BINARY_SERVER.
/// Uses OPENQUERY to access the linked server.
/// </summary>
public interface IChecklistDocumentRepository
{
    /// <summary>
    /// Gets checklist documents for a loan number.
    /// </summary>
    /// <param name="loanNo">The loan number (e.g., "CL20260934640").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of checklist documents with upload status.</returns>
    Task<List<LoanChecklistDocumentDto>> GetChecklistDocumentsAsync(
        string loanNo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the actual document content (binary) for viewing/downloading.
    /// </summary>
    /// <param name="docId">The document ID from doc_ref.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Document content as byte array, content type, and filename.</returns>
    Task<DocumentContentDto?> GetDocumentContentAsync(
        int docId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a docId to its parent loan number (cis_no) from doc_ref.
    /// Used for authorization — the caller must verify loan access before fetching content.
    /// Returns null if the document doesn't exist or has been soft-deleted.
    /// </summary>
    Task<string?> GetDocumentLoanNoAsync(
        int docId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// DTO for document binary content.
/// </summary>
public sealed record DocumentContentDto
{
    public byte[] Content { get; init; } = Array.Empty<byte>();
    public string ContentType { get; init; } = "application/octet-stream";
    public string? FileName { get; init; }
}

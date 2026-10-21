using Ebi.Alas.Api.Features.LoanApplications.Documents;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

internal sealed class EmptyChecklistReader : IChecklistDocumentReader
{
    public Task<IReadOnlyList<LoanChecklistDocumentDto>> GetChecklistDocumentsAsync(
        string loanNo,
        IReadOnlyCollection<string> idCodes,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LoanChecklistDocumentDto>>([]);

    public Task<ChecklistDocumentContent?> GetDocumentFileAsync(int docId, CancellationToken cancellationToken) =>
        Task.FromResult<ChecklistDocumentContent?>(null);
}

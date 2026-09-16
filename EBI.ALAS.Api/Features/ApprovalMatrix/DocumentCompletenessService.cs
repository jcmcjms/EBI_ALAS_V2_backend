using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EBI.ALAS.Api.Features.ApprovalMatrix;

public sealed record CompletenessResult(bool Complete, IReadOnlyList<string> Missing);

public interface IDocumentCompletenessService
{
    Task<CompletenessResult> CheckAsync(LoanApplication loan, CancellationToken ct = default);
}

public sealed class DocumentCompletenessService : IDocumentCompletenessService
{
    private readonly IChecklistDocumentRepository _checklist;
    private readonly IMemoryCache _cache;

    public DocumentCompletenessService(IChecklistDocumentRepository checklist, IMemoryCache cache)
    {
        _checklist = checklist;
        _cache = cache;
    }

    public async Task<CompletenessResult> CheckAsync(LoanApplication loan, CancellationToken ct = default)
    {
        var docs = await _cache.GetOrCreateAsync($"checklist:{loan.LoanNo}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            return await _checklist.GetChecklistDocumentsAsync(loan.LoanNo, ct);
        })!;

        var missing = docs.Where(d => d.UploadStatus != "Uploaded")
                          .Select(d => d.ChecklistDescription ?? d.IdCode).ToList();
        return new CompletenessResult(missing.Count == 0, missing);
    }
}

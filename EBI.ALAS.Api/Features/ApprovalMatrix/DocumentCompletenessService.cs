using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EBI.ALAS.Api.Features.ApprovalMatrix;

public sealed record CompletenessResult(bool Complete, IReadOnlyList<string> Missing);

public interface IDocumentCompletenessService
{
    Task<CompletenessResult> CheckAsync(LoanApplication loan, CancellationToken ct = default);

    /// <summary>LoanNo-keyed, optionally cache-bypassing variant for the
    /// background sweep and the on-demand verify endpoint.</summary>
    Task<CompletenessResult> CheckByLoanNoAsync(string loanNo, CancellationToken ct = default, bool bypassCache = false);

    /// <summary>Returns the raw checklist items for a loan so callers can
    /// inspect per-requirement upload status (e.g. push-back guard).</summary>
    Task<IReadOnlyList<LoanChecklistDocumentDto>> GetItemsByLoanNoAsync(string loanNo, CancellationToken ct = default);
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

    public Task<CompletenessResult> CheckAsync(LoanApplication loan, CancellationToken ct = default)
        => CheckByLoanNoAsync(loan.LoanNo, ct);

    public async Task<CompletenessResult> CheckByLoanNoAsync(string loanNo, CancellationToken ct = default, bool bypassCache = false)
    {
        List<LoanChecklistDocumentDto>? docs;

        if (bypassCache)
        {
            docs = await _checklist.GetChecklistDocumentsAsync(loanNo, ct);
            // Refresh the cache so subsequent reads hit the updated snapshot.
            _cache.Set($"checklist:{loanNo}", docs, new MemoryCacheEntryOptions
            {
                Size = 1,
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60),
            });
        }
        else
        {
            docs = await _cache.GetOrCreateAsync($"checklist:{loanNo}", async e =>
            {
                e.Size = 1;
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                return await _checklist.GetChecklistDocumentsAsync(loanNo, ct);
            });
        }

        var missing = (docs ?? []).Where(d => d.UploadStatus != "Uploaded")
                          .Select(d => d.ChecklistDescription ?? d.IdCode).ToList();
        return new CompletenessResult(missing.Count == 0, missing);
    }

    public async Task<IReadOnlyList<LoanChecklistDocumentDto>> GetItemsByLoanNoAsync(string loanNo, CancellationToken ct = default)
    {
        var docs = await _cache.GetOrCreateAsync($"checklist:{loanNo}", async e =>
        {
            e.Size = 1;
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            return await _checklist.GetChecklistDocumentsAsync(loanNo, ct);
        });
        return (docs ?? []).AsReadOnly();
    }
}

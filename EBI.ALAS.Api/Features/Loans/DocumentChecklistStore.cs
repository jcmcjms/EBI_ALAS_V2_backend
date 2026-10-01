using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace EBI.ALAS.Api.Features.Loans;
public interface IDocumentChecklistStore
{
    Task<IReadOnlyList<DocumentChecklist>> GetAsync(int loanId, CancellationToken ct);
    Task<IReadOnlyList<DocumentChecklist>> GetUnresolvedAsync(int loanId, CancellationToken ct);
    Task<int> CountUnresolvedAsync(int loanId, CancellationToken ct);
    Task MarkMissingAsync(int loanId, IReadOnlyCollection<string> codes, int actorId, CancellationToken ct);
    Task MarkSubmittedAsync(int loanId, IReadOnlyCollection<string> codes, int actorId, CancellationToken ct);
    Task MarkVerifiedAsync(int loanId, IReadOnlyCollection<string> codes, int actorId, CancellationToken ct);
}
public sealed class DocumentChecklistStore(AppDbContext db, ITimeProvider time) : IDocumentChecklistStore
{
    public static readonly string[] UnresolvedStatuses = ["Missing", "Pending"];
    public Task<IReadOnlyList<DocumentChecklist>> GetAsync(int loanId, CancellationToken ct) =>
        db.DocumentChecklists.AsNoTracking()
            .Where(i => i.LoanApplicationId == loanId)
            .OrderBy(i => i.Code)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<DocumentChecklist>)t.Result, ct);
    public Task<IReadOnlyList<DocumentChecklist>> GetUnresolvedAsync(int loanId, CancellationToken ct) =>
        db.DocumentChecklists.AsNoTracking()
            .Where(i => i.LoanApplicationId == loanId && UnresolvedStatuses.Contains(i.Status))
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<DocumentChecklist>)t.Result, ct);
    public Task<int> CountUnresolvedAsync(int loanId, CancellationToken ct) =>
        db.DocumentChecklists.AsNoTracking()
            .CountAsync(i => i.LoanApplicationId == loanId
                             && UnresolvedStatuses.Contains(i.Status), ct);
    public async Task MarkMissingAsync(int loanId, IReadOnlyCollection<string> codes, int actorId, CancellationToken ct)
    {
        if (codes.Count == 0) return;
        var now = time.UtcNow;
        var rows = await db.DocumentChecklists
            .Where(i => i.LoanApplicationId == loanId && codes.Contains(i.Code))
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, "Missing")
                .SetProperty(i => i.UpdatedAtUtc, now)
                .SetProperty(i => i.UpdatedById, actorId), ct);
        if (rows < codes.Count)
        {
            var existing = await db.DocumentChecklists
                .Where(i => i.LoanApplicationId == loanId && codes.Contains(i.Code))
                .Select(i => i.Code)
                .ToListAsync(ct);
            db.DocumentChecklists.AddRange(codes.Except(existing).Select(code => new DocumentChecklist
            {
                LoanApplicationId = loanId,
                Code = code,
                Name = code,
                Status = "Missing",
                UpdatedAtUtc = now,
                UpdatedById = actorId,
            }));
        }
        await db.SaveChangesAsync(ct);
    }
    public async Task MarkSubmittedAsync(int loanId, IReadOnlyCollection<string> codes, int actorId, CancellationToken ct)
    {
        if (codes.Count == 0) return;
        await db.DocumentChecklists
            .Where(i => i.LoanApplicationId == loanId && codes.Contains(i.Code)
                        && UnresolvedStatuses.Contains(i.Status))
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, "Submitted")
                .SetProperty(i => i.UpdatedAtUtc, time.UtcNow)
                .SetProperty(i => i.UpdatedById, actorId), ct);
    }
    public async Task MarkVerifiedAsync(int loanId, IReadOnlyCollection<string> codes, int actorId, CancellationToken ct)
    {
        if (codes.Count == 0) return;
        await db.DocumentChecklists
            .Where(i => i.LoanApplicationId == loanId && codes.Contains(i.Code)
                        && i.Status == "Submitted")
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, "Verified")
                .SetProperty(i => i.UpdatedAtUtc, time.UtcNow)
                .SetProperty(i => i.UpdatedById, actorId), ct);
    }
}

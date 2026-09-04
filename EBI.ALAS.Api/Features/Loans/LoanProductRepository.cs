using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

public class LoanProductRepository(AppDbContext context) : ILoanProductRepository
{
    public async Task<IReadOnlyList<LoanProduct>> GetAllAsync(CancellationToken ct = default)
    {
        // Includes retired rows so the admin "all products" view can
        // show the full catalog including retired entries. Ordered by
        // code for deterministic UI rendering.
        return await context.LoanProducts
            .AsNoTracking()
            .OrderBy(p => p.Code)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LoanProduct>> GetActiveAsync(CancellationToken ct = default)
    {
        // What the loan-creation dropdown consumes. Filter pushed to
        // SQL via the LINQ WHERE; index seek on PK (Code) is fine for
        // a small lookup table — the IsRetired filter is residual.
        return await context.LoanProducts
            .AsNoTracking()
            .Where(p => !p.IsRetired)
            .OrderBy(p => p.Code)
            .ToListAsync(ct);
    }

    public async Task<LoanProduct?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        return await context.LoanProducts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == code, ct);
    }

    public async Task<LoanProduct> UpsertAsync(
        LoanProduct product,
        bool preservePolicyFields,
        int? updatedByUserId,
        DateTime updatedDate,
        CancellationToken ct = default)
    {
        // Two distinct behaviors depending on the caller:
        //   * preservePolicyFields=true  → sync service. Webloan's row
        //     is the source of truth for code/description/IsRetired/
        //     LastSyncedAt only. Policy fields (min/max/term/fees) are
        //     never touched — ops has configured them and a sync run
        //     must not silently revert them. updatedByUserId is null
        //     (system action — no human attribution).
        //   * preservePolicyFields=false → admin endpoint. Caller
        //     supplied the full record; overwrite the row wholesale.
        //     updatedByUserId is the caller-resolved User.Id.
        //
        // Both paths bump UpdatedDate to `updatedDate` (server-supplied
        // via ITimeProvider, not DateTime.UtcNow) and set UpdatedById
        // from `updatedByUserId`. Centralizing the audit-field writes
        // here means the two callers cannot drift on what counts as
        // "the row changed" — every successful SaveChangesAsync
        // touches UpdatedDate, so the admin grid's "last modified"
        // column reflects reality across both paths.
        //
        // Why merge instead of delete+insert: the PK is the natural
        // key (Code) — the row has no surrogate Id — so a
        // delete+insert would change nothing observable but would
        // trigger audit-log noise on the AfterSaveChanges interceptor
        // for the delete. A tracked-entity merge keeps the diff clean.
        var existing = await context.LoanProducts
            .FirstOrDefaultAsync(p => p.Code == product.Code, ct);

        if (existing is null)
        {
            // New row. All fields as supplied by the caller.
            // IsRetired=true default is set on the column itself, so
            // a sync-inserted row is hidden from the dropdown until
            // the FIRST sync run explicitly marks it active. Prevents
            // a half-configured mirror from leaking into production.
            //
            // Audit fields: stamp UpdatedDate at insert time so a
            // brand-new row already carries a "when did this enter the
            // mirror?" signal. UpdatedById is whatever the caller
            // supplied — null for sync, the admin's id for admin
            // inserts (none today, but the schema supports it).
            product.UpdatedDate = updatedDate;
            product.UpdatedById = updatedByUserId;
            await context.LoanProducts.AddAsync(product, ct);
        }
        else
        {
            // Existing row. Always update the sync-owned fields.
            existing.Description = product.Description;
            existing.IsRetired = product.IsRetired;
            existing.LastSyncedAt = product.LastSyncedAt;

            if (!preservePolicyFields)
            {
                existing.MinAmount = product.MinAmount;
                existing.MaxAmount = product.MaxAmount;
                existing.MinTermDays = product.MinTermDays;
                existing.MaxTermDays = product.MaxTermDays;
                existing.NotarialFee = product.NotarialFee;
                existing.DocStampFee = product.DocStampFee;
                existing.InsuranceFee = product.InsuranceFee;
                existing.AdvanceInterestRate = product.AdvanceInterestRate;
            }

            // Audit fields. ALWAYS bump them on every successful save
            // — even on the sync "preserved" path where none of the
            // business columns change, the row was still re-stamped
            // and the audit log will reflect that. This keeps
            // UpdatedDate in sync with reality and gives ops a single
            // "when did this row last move?" answer.
            existing.UpdatedDate = updatedDate;
            existing.UpdatedById = updatedByUserId;
        }

        await context.SaveChangesAsync(ct);

        // Return the post-merge state — caller usually wants the
        // authoritative row, not the input DTO.
        return existing ?? product;
    }

    public async Task<bool> DeleteAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        var existing = await context.LoanProducts
            .FirstOrDefaultAsync(p => p.Code == code, ct);

        if (existing is null) return false;

        context.LoanProducts.Remove(existing);
        await context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ExistsActiveByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        return await context.LoanProducts
            .AsNoTracking()
            .AnyAsync(p => p.Code == code && !p.IsRetired, ct);
    }
}

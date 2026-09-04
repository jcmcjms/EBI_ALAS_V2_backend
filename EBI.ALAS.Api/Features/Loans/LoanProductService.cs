using EBI.ALAS.Api.Common.Time;

namespace EBI.ALAS.Api.Features.Loans;

public class LoanProductService(
    ILoanProductRepository repository,
    ILoanProductSyncService syncService,
    ITimeProvider timeProvider) : ILoanProductService
{
    // Hard business ceiling: no product may offer more than 7 years
    // (84 months) of term. Enforced here AND in the validator so the
    // CreateLoan and admin-update paths both reject violations.
    public const int AbsoluteMaxTermMonths = 84;

    public async Task<IReadOnlyList<LoanProductResponse>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await repository.GetAllAsync(ct);
        return rows.Select(ToResponse).ToList();
    }

    public async Task<IReadOnlyList<LoanProductResponse>> GetActiveAsync(CancellationToken ct = default)
    {
        var rows = await repository.GetActiveAsync(ct);
        return rows.Select(ToResponse).ToList();
    }

    public async Task<LoanProductResponse?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var row = await repository.GetByCodeAsync(code, ct);
        return row is null ? null : ToResponse(row);
    }

    public async Task<LoanProductResponse?> UpdateAsync(
        string code,
        UpdateLoanProductRequest request,
        int updatedByUserId,
        CancellationToken ct = default)
    {
        // Mirror rows for the policy fields can only be configured
        // once the sync has run and pulled the product into ALAS.
        // Hitting UPDATE on a code that doesn't exist locally is a
        // 404 — ops should run sync first, then edit.
        var existing = await repository.GetByCodeAsync(code, ct);
        if (existing is null) return null;

        // Defense-in-depth validation. FluentValidation on the
        // request DTO catches most of these, but the service is the
        // last gate before the DB — re-checking here means a
        // programmatically-bypassed validator (e.g. a future internal
        // caller) still cannot violate the business rules.
        ValidatePolicyFields(request);

        // Sync-owned fields (IsRetired, Description, LastSyncedAt,
        // Code) are preserved on this path. Only the policy fields
        // ops is editing change. This mirrors the sync's
        // preservePolicyFields=true semantics, just inverted: the
        // admin path preserves the sync-owned fields and overwrites
        // the policy fields.
        existing.MinAmount = request.MinAmount;
        existing.MaxAmount = request.MaxAmount;
        existing.MinTermMonths = request.MinTermMonths;
        existing.MaxTermMonths = request.MaxTermMonths;
        existing.NotarialFee = request.NotarialFee;
        existing.DocStampFee = request.DocStampFee;
        existing.InsuranceFee = request.InsuranceFee;
        existing.AdvanceInterestRate = request.AdvanceInterestRate;

        // UpsertAsync with preservePolicyFields=false is what writes
        // the updated row — the merge helper keeps the logic in one
        // place. updatedByUserId comes from the endpoint (the
        // caller's User.Id, resolved from the ClaimsPrincipal); the
        // server clock (ITimeProvider — never DateTime.UtcNow) is the
        // UpdatedDate source so all writes are testable and timezone
        // handling stays centralized in Common/Time/.
        var updated = await repository.UpsertAsync(
            existing,
            preservePolicyFields: false,
            updatedByUserId: updatedByUserId,
            updatedDate: timeProvider.UtcNow,
            ct);
        return ToResponse(updated);
    }

    public async Task<LoanProductSyncResult> SyncFromWebloanAsync(CancellationToken ct = default)
    {
        return await syncService.SyncAsync(ct);
    }

    // ─── Helpers ────────────────────────────────────────────────────────
    private static LoanProductResponse ToResponse(LoanProduct p) => new(
        p.Code,
        p.Description,
        p.MinAmount,
        p.MaxAmount,
        p.MinTermMonths,
        p.MaxTermMonths,
        p.NotarialFee,
        p.DocStampFee,
        p.InsuranceFee,
        p.AdvanceInterestRate,
        p.IsRetired,
        p.LastSyncedAt,
        p.UpdatedDate,
        p.UpdatedById,
        // Resolved from the navigation property when it is loaded
        // (sync-driven rows have null UpdatedById, so the name is
        // null too — the UI shows "system" or hides the column for
        // those rows). The repository does NOT eagerly include
        // UpdatedBy today; the admin grid query that needs the name
        // should .Include(p => p.UpdatedBy) at the call site.
        p.UpdatedBy is null
            ? null
            : $"{p.UpdatedBy.FirstName} {p.UpdatedBy.LastName}");

    private static void ValidatePolicyFields(UpdateLoanProductRequest r)
    {
        if (r.MinAmount < 0)
            throw new ArgumentException("MinAmount cannot be negative.", nameof(r));
        if (r.MaxAmount < r.MinAmount)
            throw new ArgumentException(
                $"MaxAmount ({r.MaxAmount}) must be >= MinAmount ({r.MinAmount}).", nameof(r));
        if (r.MinTermMonths < 0)
            throw new ArgumentException("MinTermMonths cannot be negative.", nameof(r));
        if (r.MaxTermMonths < r.MinTermMonths)
            throw new ArgumentException(
                $"MaxTermMonths ({r.MaxTermMonths}) must be >= MinTermMonths ({r.MinTermMonths}).", nameof(r));
        if (r.MaxTermMonths > AbsoluteMaxTermMonths)
            throw new ArgumentException(
                $"MaxTermMonths ({r.MaxTermMonths}) cannot exceed the absolute ceiling of {AbsoluteMaxTermMonths} months (7 years).", nameof(r));
        if (r.NotarialFee < 0 || r.DocStampFee < 0 || r.InsuranceFee < 0)
            throw new ArgumentException("Fees cannot be negative.", nameof(r));
        if (r.AdvanceInterestRate < 0 || r.AdvanceInterestRate > 1m)
            throw new ArgumentException(
                "AdvanceInterestRate must be between 0 and 1 (e.g. 0.12 for 12% p.a.).", nameof(r));
    }
}

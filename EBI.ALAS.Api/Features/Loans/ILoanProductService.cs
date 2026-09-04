namespace EBI.ALAS.Api.Features.Loans;

// ─── DTOs ──────────────────────────────────────────────────────────────────
// Read-side response shape used by every endpoint that returns a
// product. Mirrors the entity 1:1 so the admin form can round-trip
// without field renaming. Decimals are kept as `decimal` (not `double`)
// to avoid the binary-float conversion noise that would otherwise show
// up in the JSON.
public record LoanProductResponse(
    string Code,
    string Description,
    decimal MinAmount,
    decimal MaxAmount,
    int MinTermDays,
    int MaxTermDays,
    decimal NotarialFee,
    decimal DocStampFee,
    decimal InsuranceFee,
    decimal AdvanceInterestRate,
    bool IsRetired,
    DateTime LastSyncedAt,
    DateTime UpdatedDate,
    int? UpdatedById,
    string? UpdatedByName);

// Admin write shape. The sync service writes via the repository
// directly; this DTO is the human-facing surface for ops to configure
// policy fields through the ALAS admin UI. Code is the natural key —
// ops picks the existing product from a list and edits the rest.
public record UpdateLoanProductRequest(
    decimal MinAmount,
    decimal MaxAmount,
    int MinTermDays,
    int MaxTermDays,
    decimal NotarialFee,
    decimal DocStampFee,
    decimal InsuranceFee,
    decimal AdvanceInterestRate);

// Sync summary returned to the manual `/sync` endpoint so ops can see
// what the run did without grepping logs. `Added` is a brand-new row
// in ALAS; `Updated` is an existing row whose IsRetired or Description
// changed; `Preserved` is a row whose policy fields are unchanged (the
// sync leaves them alone).
public record LoanProductSyncResult(
    int Added,
    int Updated,
    int Preserved,
    DateTime SyncedAt);

// ─── Service contract ──────────────────────────────────────────────────────
public interface ILoanProductService
{
    Task<IReadOnlyList<LoanProductResponse>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LoanProductResponse>> GetActiveAsync(CancellationToken ct = default);
    Task<LoanProductResponse?> GetByCodeAsync(string code, CancellationToken ct = default);

    // Returns null when the code does not exist in the mirror. The
    // endpoint layer maps that to 404.
    //
    // `updatedByUserId` is the caller's User.Id (resolved from the
    // ClaimsPrincipal at the endpoint layer). It is recorded on the
    // UpdatedById column so admin edits stay attributable. The sync
    // path does not call this — sync writes via the repository
    // directly and leaves UpdatedById null (system action).
    Task<LoanProductResponse?> UpdateAsync(
        string code,
        UpdateLoanProductRequest request,
        int updatedByUserId,
        CancellationToken ct = default);

    // Triggers a manual sync run. Used by the admin "Sync now" button
    // AND by the hosted background service. Returns the summary so
    // the caller can show "Synced 7 products (2 new, 1 retired)" in
    // the UI or log.
    Task<LoanProductSyncResult> SyncFromWebloanAsync(CancellationToken ct = default);
}

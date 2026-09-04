namespace EBI.ALAS.Api.Features.Loans;

// Repository contract for the LoanProducts table.
//
// The implementation targets AppDbContext (ALASv2_DB), NOT webloan.
// LoanProduct is an ALAS-owned mirror of webloan's loan_product
// catalog; the source of truth for the policy fields (min/max/term/
// fees) is ALAS. The retirement flag is derived from webloan's
// `expiration IS NOT NULL` by the sync service.
public interface ILoanProductRepository
{
    // ─── Read paths (used by the loan-creation form and admin screens) ──
    // All products, both active and retired. Admin-only views typically.
    Task<IReadOnlyList<LoanProduct>> GetAllAsync(CancellationToken ct = default);

    // Only non-retired products. This is what the loan-creation
    // dropdown consumes — encoders can never pick a retired product.
    Task<IReadOnlyList<LoanProduct>> GetActiveAsync(CancellationToken ct = default);

    // Single-row fetch by natural key. Returns null when the code does
    // not exist (mirror row never synced). Used by the validator and
    // the admin edit form.
    Task<LoanProduct?> GetByCodeAsync(string code, CancellationToken ct = default);

    // ─── Write paths (Admin / sync) ─────────────────────────────────────
    // Upsert by natural key. Called by:
    //   * The sync service when webloan introduces a new product or
    //     changes a retirement flag.
    //   * The admin update endpoint when ops changes the policy fields.
    //
    // `preservePolicyFields` controls whether the upsert overwrites
    // MinAmount/MaxAmount/TermMonths/fees (true = leave them alone,
    // false = overwrite with the supplied values). The sync passes
    // true so it never wipes out ops-configured policy on a refresh;
    // the admin endpoint passes false to write the new policy.
    Task<LoanProduct> UpsertAsync(
        LoanProduct product,
        bool preservePolicyFields,
        CancellationToken ct = default);

    // Hard delete by code. Used by the admin "remove" path; the sync
    // service does NOT call this (a missing webloan product is
    // represented by IsRetired=true, not by row removal — preserves
    // audit trail if ops later wants to see what used to be offered).
    Task<bool> DeleteAsync(string code, CancellationToken ct = default);

    // True when a row with the given code exists and IsRetired=false.
    // Used by CreateLoanValidator to short-circuit before doing the
    // full row fetch.
    Task<bool> ExistsActiveByCodeAsync(string code, CancellationToken ct = default);
}

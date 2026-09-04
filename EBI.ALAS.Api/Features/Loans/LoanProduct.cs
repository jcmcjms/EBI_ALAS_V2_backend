using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.Loans;

// ALAS-owned mirror of the webloan.loan_product catalog.
//
// webloan tells us WHICH products exist and WHEN they retire (expiration).
// ALAS owns the policy data — what the loan is allowed to do and what it
// costs. This split exists because webloan is read-only (the
// WebLoanReadOnlyInterceptor blocks any write into it) and the policy
// fields are maintained by bank ops through the ALAS admin UI.
//
// PK is the webloan `id_code` (e.g. "C35"), not a surrogate int. Two
// reasons:
//   * Sync becomes a trivial upsert by natural key — no "did this row
//     move?" bookkeeping when a new webloan product is synced in.
//   * LoanApplications already store the product code as a string
//     (LoanApplication.Product) — promoting the FK relationship to a
//     real FK here would force a string→int rewrite of every loan
//     record, which we want to avoid.
//
// The mirror is fully ALAS-owned on the policy columns: webloan is never
// the source of truth for min/max/term/fees. A webloan change to those
// columns would NOT propagate to ALAS — only the existence and
// retirement signals do.
[Table("LoanProducts")]
public class LoanProduct
{
    // webloan.loan_product.id_code (e.g. "C35", "C23"). PK so the sync
    // upsert is idempotent — re-running the same webloan row produces
    // the same ALAS row.
    [Column("Code")]
    public string Code { get; set; } = string.Empty;

    [Column("Description")]
    public string Description { get; set; } = string.Empty;

    // ── Eligibility bounds (ALAS-owned) ───────────────────────────────
    // Hard floor on the principal an encoder can request for this
    // product. Validated server-side in CreateLoanValidator.
    [Column("MinAmount")]
    public decimal MinAmount { get; set; }

    // Hard ceiling on the principal. Capped at app level via the
    // validator, not the column — the column is decimal(18,2) to allow
    // product-specific ceilings up to ~9.99 trillion PHP without
    // overflow concerns.
    [Column("MaxAmount")]
    public decimal MaxAmount { get; set; }

    // Shortest term an encoder can request, in whole months. Product
    // rules in PH banking are typically in months; if a product ever
    // needs day-granularity, the sync layer would have to convert.
    [Column("MinTermMonths")]
    public int MinTermMonths { get; set; }

    // Longest term, in months. The business has a hard 7-year (84-month)
    // ceiling on all products; the validator enforces that on top of
    // this per-product value.
    [Column("MaxTermMonths")]
    public int MaxTermMonths { get; set; }

    // ── Fees & charges (ALAS-owned, all PHP) ──────────────────────────
    // Flat-fee columns. Each loan disbursement shows these as line
    // items in the preview and deducts them (along with advance
    // interest) from the gross proceeds. If a product ever needs
    // percentage-based fees, add a separate column rather than overload
    // these — the disbursement calculation is column-typed today.
    [Column("NotarialFee")]
    public decimal NotarialFee { get; set; }

    [Column("DocStampFee")]
    public decimal DocStampFee { get; set; }

    [Column("InsuranceFee")]
    public decimal InsuranceFee { get; set; }

    // ── Interest model (ALAS-owned) ───────────────────────────────────
    // Advance-interest annual rate, decimal(9,6). "Advance" in PH
    // banking means interest is deducted from proceeds at disbursement
    // (the borrower receives Principal - Interest - Fees). The
    // disbursement service multiplies this by principal and
    // (termMonths / 12) to compute the deduction. 0.120000 = 12% p.a.
    //
    // We do NOT store a per-term rate table; products with rate
    // brackets need a separate child table (out of scope for this
    // slice).
    [Column("AdvanceInterestRate")]
    public decimal AdvanceInterestRate { get; set; }

    // ── Sync state (ALAS-owned) ───────────────────────────────────────
    // Mirrored from webloan.loan_product.expiration IS NOT NULL at sync
    // time. Stored as a boolean (not a date) because the only consumer
    // question is "should I show this in the dropdown?" — and exposing
    // the retirement date to encoders is unnecessary noise.
    //
    // The sync updates this on every run, so the value tracks webloan
    // within one sync interval (configurable, default 6h).
    [Column("IsRetired")]
    public bool IsRetired { get; set; }

    // Timestamp of the last successful sync for this row. Useful for
    // diagnosing "why is this product still showing as active?" —
    // ops can see the lag without querying the webloan DB.
    [Column("LastSyncedAt")]
    public DateTime LastSyncedAt { get; set; }
}

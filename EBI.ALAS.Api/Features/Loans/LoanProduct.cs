using System.ComponentModel.DataAnnotations.Schema;
using EBI.ALAS.Api.Features.Auth;

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

    // Shortest term an encoder can request, in whole days.
    [Column("MinTermDays")]
    public int MinTermDays { get; set; }

    // Longest term, in days. The business has a hard 7-year (2,555-day)
    // ceiling on all products; the validator enforces that on top of
    // this per-product value.
    [Column("MaxTermDays")]
    public int MaxTermDays { get; set; }

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
    // (termDays / 365) to compute the deduction. 0.120000 = 12% p.a.
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

    // ── Audit (ALAS-owned) ───────────────────────────────────────────
    // Last-modification timestamp across BOTH the sync path and the
    // admin update path. Source of truth for "when did this row last
    // change?". Distinct from LastSyncedAt, which is only bumped when
    // webloan-side fields are refreshed — UpdatedDate is bumped on
    // every successful SaveChangesAsync, including admin edits of
    // policy fields.
    //
    // Stored as datetime2 (not datetime) — same type used by
    // LoanApplication.LastActionDate and User.CreatedAt, so the
    // audit-log timeline across the system is homogeneous.
    [Column("UpdatedDate")]
    public DateTime UpdatedDate { get; set; }

    // User who last modified the row. NULL on rows inserted by the
    // background sync service — system actions have no human
    // attribution. The endpoint layer requires CanManageLoanProduct
    // and passes the caller's user id, so UpdatedById is non-null on
    // any admin-driven change.
    //
    // Stored as int (matching LoanApplication.CreatedById /
    // LoanAction.ActionByUserId) rather than nvarchar(username) so
    // renames don't rewrite history. The FK is set with
    // DeleteBehavior.Restrict in AppDbContext to mirror the
    // LoanApplication.CreatedBy convention — orphaning a user must
    // never silently rewrite audit history.
    [Column("UpdatedById")]
    public int? UpdatedById { get; set; }

    // Navigation property — populated by .Include(p => p.UpdatedBy)
    // on read paths that need to surface the modifier's name. Not
    // eagerly loaded by default; the response DTO handles the
    // nullable case (UpdatedByName may be null for sync-driven rows).
    public User? UpdatedBy { get; set; } = null!;
}

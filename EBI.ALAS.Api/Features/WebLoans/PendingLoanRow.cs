using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;

// ─── PendingLoanRow ──────────────────────────────────────────────────────────
//
// Keyless projection entity used ONLY by
// WebLoanRepository.GetPendingLoansAsync. Carries every column the
// consolidated pending-loan SQL projects — a five-table LEFT JOIN against
// pre_loan_data, loan_data, loan_product, loan_purpose, loan_acct_info,
// and check_list_data:
//
//   SELECT
//     pld.bch, pld.acct_no, pld.loan_no,            -- identifiers
//     ld.principal, ld.granted_rate,                -- loan_data scalars
//     ld.total_amortization,
//     ld.date_granted, ld.date_maturity,
//     ld.creation_type,
//     CASE ld.creation_type … END                    -- creation_type_label
//       AS creation_type_label,
//     DATEDIFF(DAY, ld.date_granted,                 -- total_term_days
//              ld.date_maturity)
//       AS total_term_days,
//     ld.loan_product + ' - ' + ISNULL(lp.description, '')
//       AS product_with_desc,
//     lp2.description                                  AS loan_purpose,
//     cld.description                                 AS nthp,
//     cld.expiration                                  AS nthp_date
//
// Why a separate entity (mirroring OutstandingLoanRow):
//   * `creation_type_label`, `total_term_days`, `product_with_desc` are
//     DERIVED columns, not real webloan columns. EF enforces that every
//     mapped property on the entity be projected by the raw SQL; a
//     `[NotMapped]` property would never be populated; a `[Column]`
//     attribute would be a lie because the columns do not exist in
//     `dbo.pre_loan_data` (or any of the joined tables).
//   * Materializing through a dedicated keyless entity whose columns
//     match the SELECT list 1:1 lets EF's materializer populate each
//     property positionally — no surprises, no missing-column errors.
//
// No [Table] attribute is needed because the entity is keyless and never
// maps to a single underlying table — it is a projection shape only.
public class PendingLoanRow
{
    // ─── pre_loan_data identifiers (always non-null) ─────────────────────
    [Column("bch")] public string BranchCode { get; set; } = string.Empty;
    [Column("acct_no")] public string AccountNo { get; set; } = string.Empty;
    [Column("loan_no")] public string LoanNo { get; set; } = string.Empty;

    // ─── loan_data scalars (LEFT JOIN → NULL when no ledger row exists) ──
    [Column("principal")] public decimal? Principal { get; set; }
    [Column("granted_rate")] public decimal? GrantedRate { get; set; }
    [Column("total_amortization")] public int? TotalAmortization { get; set; }
    [Column("date_granted")] public DateTime? DateGranted { get; set; }
    [Column("date_maturity")] public DateTime? DateMaturity { get; set; }
    [Column("creation_type")] public byte? CreationType { get; set; }

    // ─── Derived columns (CASE / DATEDIFF / CONCAT in SQL) ───────────────
    // Bound to the SELECT-list alias `creation_type_label`. Mirrors the
    // original SQL's CASE block: 0=New, 1=Reloan, 2=Restructured,
    // 6=Additional Loan, anything else (including NULL when no
    // loan_data row exists) → 'Unknown'.
    [Column("creation_type_label")]
    public string CreationTypeLabel { get; set; } = "Unknown";

    // Bound to the SELECT-list alias `total_term_days`. Replaces the
    // legacy `total_amortization * 30` approximation with the exact
    // day count via DATEDIFF(DAY, date_granted, date_maturity). NULL
    // when either date is NULL (e.g. partial loan_data row).
    [Column("total_term_days")]
    public int? TotalTermDays { get; set; }

    // Bound to the SELECT-list alias `product_with_desc`:
    //
    //   ld.loan_product + ' - ' + ISNULL(lp.description, '')
    //
    // Sourced from a LEFT JOIN to webloan.dbo.loan_product on
    // (ld.loan_product = lp.id_code). When no loan_product row matches
    // (orphaned product code in loan_data, or the product was retired
    // but pending loans still reference it), the LEFT JOIN miss leaves
    // lp.description NULL — ISNULL coerces it to '' so the result is
    // at least "<code> - " instead of a full NULL. The service layer
    // trims that trailing separator when projecting to the DTO so the
    // UI never sees a dangling " - ".
    [Column("product_with_desc")]
    public string? ProductWithDescription { get; set; }

    // ─── loan_purpose (LEFT JOIN, nullable description) ──────────────────
    [Column("loan_purpose")] public string? LoanPurpose { get; set; }

    // ─── check_list_data NTHP (LEFT JOIN, both fields nullable) ──────────
    // Sourced from a LEFT JOIN chain:
    //
    //   loan_acct_info AS la ON pld.acct_no = la.acct_no AND pld.bch = la.bch
    //   check_list_data AS cld ON la.cis_no = cld.cis_no
    //                             AND cld.check_list_item = 'CCR07'
    //
    // The (bch, acct_no) → cis_no hop on loan_acct_info is the
    // authoritative CIS for the account — the anti-enumeration guard at
    // the service layer ensures la.cis_no equals the URL's cisNo, so
    // this matches the old single-row CCR07 fetch exactly.
    //
    // Cartesian-product caveat: if check_list_data carries more than
    // one CCR07 row for the same cis_no (different vintages), this
    // query produces one row per CCR07 match per pre_loan_data row —
    // i.e. a true cartesian product. The service layer de-duplicates
    // NTHP by reading it from the first row only (all rows in the
    // group carry the same pre_loan_data + loan_data values, so the
    // per-loan projection is unaffected). To suppress the duplicates
    // at the SQL level, wrap the check_list_data join in a subquery
    // filtered to TOP 1.
    [Column("nthp")] public string? Nthp { get; set; }
    [Column("nthp_date")] public DateTime? NthpDate { get; set; }
}
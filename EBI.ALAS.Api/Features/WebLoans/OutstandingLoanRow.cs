using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;

// ─── OutstandingLoanRow ──────────────────────────────────────────────────────
// Keyless projection entity used ONLY by WebLoanRepository.GetOutstandingLoansAsync.
//
// It carries the full set of loan_data columns the raw query selects PLUS
// the derived `computed_amort_amount` column produced by the LEFT JOIN to
// dbo.amort_data + CASE expression:
//
//   CASE
//     WHEN ld.loan_product IN ('C35','C23') THEN ld.principal
//     ELSE ad.total_amort
//   END AS computed_amort_amount
//
// Why a separate entity (instead of adding `ComputedAmortAmount` to
// LoanData)?
//   * `computed_amort_amount` is a DERIVED column, not a real webloan
//     column. EF enforces that every mapped property on the entity be
//     projected by the raw SQL; a `[NotMapped]` property would never be
//     populated; a `[Column]` attribute would be a lie because the
//     column doesn't exist in `dbo.loan_data`.
//   * Materializing through a dedicated keyless entity whose columns
//     match the SELECT list 1:1 lets EF's materializer populate each
//     property positionally — no surprises, no missing-column errors.
//
// No [Table] attribute is needed because the entity is keyless and never
// maps to a single underlying table — it is a projection shape only.
public class OutstandingLoanRow
{
    // ─── LoanData fields (mirrors dbo.loan_data columns) ─────────────────
    [Column("bk")] public string BankCode { get; set; } = string.Empty;
    [Column("bch")] public string BranchCode { get; set; } = string.Empty;
    [Column("acct_no")] public string AccountNo { get; set; } = string.Empty;
    [Column("loan_no")] public string? LoanNo { get; set; }

    [Column("loan_product")] public string? ProductCode { get; set; }
    [Column("payment_interval")] public int? PaymentInterval { get; set; }
    [Column("total_amortization")] public int? TotalAmortization { get; set; }
    [Column("granted_rate")] public decimal? GrantedRate { get; set; }
    [Column("effective_rate")] public decimal? EffectiveRate { get; set; }
    [Column("cat_loan_purpose")] public string? Purpose { get; set; }
    [Column("principal")] public decimal? Principal { get; set; }
    [Column("applied_principal")] public decimal? AppliedPrincipal { get; set; }

    [Column("principal_bal")] public decimal? PrincipalBalance { get; set; }
    [Column("amort_amount")] public decimal? AmortizationAmount { get; set; }
    [Column("over_bal")] public decimal? OutstandingBalance { get; set; }
    [Column("date_granted")] public DateTime? DateGranted { get; set; }
    [Column("date_maturity")] public DateTime? DateMaturity { get; set; }
    [Column("loan_status")] public byte? StatusCode { get; set; }

    [Column("close_date")] public DateTime? CloseDate { get; set; }
    [Column("creation_type")] public byte? CreationType { get; set; }

    // ─── Derived column from the CASE expression ─────────────────────────
    // Bound to the SELECT-list alias `computed_amort_amount`. The CASE
    // evaluates to ld.principal for C35/C23 products and ad.total_amort
    // (from amort_data, amort_no = 1) for everything else. LEFT JOIN miss
    // → NULL for non-C35/C23 products with no amort_data row.
    [Column("computed_amort_amount")]
    public decimal? ComputedAmortAmount { get; set; }

    // ─── Derived product-with-description string ─────────────────────────
    // Bound to the SELECT-list alias `product_with_desc`:
    //
    //   ld.loan_product + ' - ' + ISNULL(lp.description, '')
    //
    // Sourced from a second LEFT JOIN to webloan.dbo.loan_product on
    // (ld.loan_product = lp.id_code). When no loan_product row matches
    // (orphaned product code in loan_data, or the product was retired
    // but loans are still open), the LEFT JOIN miss leaves lp.description
    // NULL — ISNULL coerces it to '' so the result is at least
    // "<code> - " instead of a full NULL. The service layer trims that
    // trailing separator when projecting to the DTO so the UI never sees
    // a dangling " - ".
    [Column("product_with_desc")]
    public string? ProductWithDescription { get; set; }
}
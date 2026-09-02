using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;
[Table("pre_loan_data", Schema = "dbo")]
public class PreLoanData
{
    // ─── Identifiers ──────────────────────────────────────────────────────
    [Column("bk")] public string BankCode { get; set; } = string.Empty;
    [Column("bch")] public string BranchCode { get; set; } = string.Empty;
    [Column("acct_no")] public string AccountNo { get; set; } = string.Empty;
    [Column("loan_no")] public string LoanNo { get; set; } = string.Empty;

    // ─── Workflow dates ───────────────────────────────────────────────────
    // The pending-loan query filters WHERE all four of these are NULL —
    // meaning the loan is in flight (prepared, not yet approved/released).
    [Column("prepared_date")] public DateTime? PreparedDate { get; set; }
    [Column("approved_date")] public DateTime? ApprovedDate { get; set; }
    [Column("released_date")] public DateTime? ReleasedDate { get; set; }
    [Column("void_date")] public DateTime? VoidDate { get; set; }

    // NOTE: underwriter-facing fields (principal, granted_rate,
    // total_amortization, loan_product, cat_loan_purpose) live on
    // loan_data, NOT pre_loan_data. The original SQL joins
    // pre_loan_data → loan_data to surface them. The service layer
    // composes those via GetLoanDataByLoanNoAsync.
}
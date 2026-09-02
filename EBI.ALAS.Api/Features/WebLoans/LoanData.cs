using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;
[Table("loan_data", Schema = "dbo")]
public class LoanData
{
    [Column("bk")] public string BankCode { get; set; } = string.Empty;
    [Column("bch")] public string BranchCode { get; set; } = string.Empty;
    [Column("acct_no")] public string AccountNo { get; set; } = string.Empty;

    [Column("loan_no")] public string? LoanNo { get; set; }

    // Loan Information section
    [Column("loan_product")] public string? ProductCode { get; set; }
    [Column("payment_interval")] public int? PaymentInterval { get; set; }
    [Column("total_amortization")] public int? TotalAmortization { get; set; }
    [Column("granted_rate")] public decimal? GrantedRate { get; set; }
    [Column("effective_rate")] public decimal? EffectiveRate { get; set; }
    [Column("cat_loan_purpose")] public string? Purpose { get; set; }
    [Column("principal")] public decimal? Principal { get; set; }
    [Column("applied_principal")] public decimal? AppliedPrincipal { get; set; }

    // Outstanding Loans section
    [Column("principal_bal")] public decimal? PrincipalBalance { get; set; }
    [Column("amort_amount")] public decimal? AmortizationAmount { get; set; }
    [Column("over_bal")] public decimal? OutstandingBalance { get; set; }
    // Note: the computed amortization amount surfaced on the outstanding-
    // loans endpoint (CASE WHEN loan_product IN ('C35','C23') THEN
    // principal ELSE ad.total_amort END, joined to dbo.amort_data with
    // amort_no = 1) is NOT modelled here — it is a derived column, not a
    // webloan column, and EF will reject any FromSql that fails to
    // project every mapped property. The repository carries a dedicated
    // projection record type for this purpose; see
    // WebLoanRepository.GetOutstandingLoansAsync.
    [Column("date_granted")] public DateTime? DateGranted { get; set; }
    [Column("date_maturity")] public DateTime? DateMaturity { get; set; }
    [Column("loan_status")] public byte? StatusCode { get; set; }

    // Close/payoff detection
    [Column("close_date")] public DateTime? CloseDate { get; set; }
    [Column("creation_type")] public byte? CreationType { get; set; }
}

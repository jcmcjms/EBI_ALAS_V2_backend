using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;

/// <summary>
/// Maps to dbo.loan_data in the WebLoan database — individual loan (PN) records.
/// KEYLESS by design: loan_no is nullable in webloan (ledger rows have no PN),
/// and this context never tracks or writes. Only columns needed by ALAS are mapped.
/// </summary>
[Table("loan_data", Schema = "dbo")]
public class LoanData
{
    [Column("bk")] public string BankCode { get; set; } = string.Empty;
    [Column("bch")] public string BranchCode { get; set; } = string.Empty;
    [Column("acct_no")] public string AccountNo { get; set; } = string.Empty;

    /// <summary>Promissory Note number. NULL for ledger/non-PN rows.</summary>
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
    [Column("date_granted")] public DateTime? DateGranted { get; set; }
    [Column("date_maturity")] public DateTime? DateMaturity { get; set; }
    [Column("loan_status")] public byte? StatusCode { get; set; }

    // Close/payoff detection
    [Column("close_date")] public DateTime? CloseDate { get; set; }

    /// <summary>
    /// Loan classification code: 0 New Loan, 1 Reloan, 2 Reconstructed,
    /// 3 Continuation, 4 Extension, 5 Renewal, 6 Additional Loan.
    /// Labels resolved via WebLoanCreationTypes.
    /// </summary>
    [Column("creation_type")] public byte? CreationType { get; set; }
}

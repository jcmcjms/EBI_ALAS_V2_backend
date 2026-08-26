using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;

/// <summary>
/// Maps to dbo.loan_acct_info in the WebLoan database — loan account master per client.
/// Links cis_no (client) to acct_no (loan account).
/// </summary>
[Table("loan_acct_info", Schema = "dbo")]
public class LoanAcctInfo
{
    [Column("bk")] public string BankCode { get; set; } = string.Empty;
    [Column("bch")] public string BranchCode { get; set; } = string.Empty;
    [Column("acct_no")] public string AccountNo { get; set; } = string.Empty;
    [Column("name")] public string? Name { get; set; }
    [Column("cis_no")] public string CisNo { get; set; } = string.Empty;
    [Column("credit_limit")] public decimal? CreditLimit { get; set; }
    [Column("used_credit")] public decimal? UsedCredit { get; set; }
    [Column("borrower_type")] public string? BorrowerType { get; set; }

    // MIS Agency grouping
    [Column("cat_mis_group")] public string? MisGroup { get; set; }
}

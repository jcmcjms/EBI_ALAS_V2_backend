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

    // MIS Agency grouping — primary path (e.g. "INDIV/SAL" for individual, salary-based).
    [Column("cat_mis_group")] public string? MisGroup { get; set; }

    // MIS Agency grouping — secondary path (e.g. "CLD/C00/C0001/C000101/C000101007").
    // Resolved against dbo.mis_group.path to obtain the agency description
    // (e.g. "DEPED LIANGA").
    [Column("cat_mis_group2")] public string? MisGroup2 { get; set; }

    // Requesting officer path (e.g. "CLD/C00/C0001/C000101"). Resolved against
    // dbo.mis_group.path WHERE group_no = 2 to obtain the officer's name
    // (e.g. "ALDREX JOEY L. CEZAR").
    [Column("solicitor")] public string? Solicitor { get; set; }

    // Account-level address components (different from cis_info home address).
    [Column("add1")] public string? Add1 { get; set; }
    [Column("add2")] public string? Add2 { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
namespace EBI.ALAS.Api.Features.WebLoans;
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
    [Column("cat_mis_group")] public string? MisGroup { get; set; }
    [Column("cat_mis_group2")] public string? MisGroup2 { get; set; }
    [Column("solicitor")] public string? Solicitor { get; set; }
    [Column("add1")] public string? Add1 { get; set; }
    [Column("add2")] public string? Add2 { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
namespace EBI.ALAS.Api.Features.WebLoans;
[Table("amort_data", Schema = "dbo")]
public class AmortData
{
    [Column("bk")] public string BankCode { get; set; } = string.Empty;
    [Column("bch")] public string BranchCode { get; set; } = string.Empty;
    [Column("acct_no")] public string AccountNo { get; set; } = string.Empty;
    [Column("loan_no")] public string? LoanNo { get; set; }
    [Column("amort_no")] public int? AmortNo { get; set; }
    [Column("total_amort")] public decimal? TotalAmort { get; set; }
}

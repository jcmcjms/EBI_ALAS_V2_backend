using System.ComponentModel.DataAnnotations.Schema;
namespace EBI.ALAS.Api.Features.WebLoans;
[Table("pre_loan_data", Schema = "dbo")]
public class PreLoanData
{
    [Column("bk")] public string BankCode { get; set; } = string.Empty;
    [Column("bch")] public string BranchCode { get; set; } = string.Empty;
    [Column("acct_no")] public string AccountNo { get; set; } = string.Empty;
    [Column("loan_no")] public string LoanNo { get; set; } = string.Empty;
    [Column("prepared_date")] public DateTime? PreparedDate { get; set; }
    [Column("approved_date")] public DateTime? ApprovedDate { get; set; }
    [Column("approved_by")] public string? ApprovedBy { get; set; }
    [Column("released_date")] public DateTime? ReleasedDate { get; set; }
    [Column("released_by")] public string? ReleasedBy { get; set; }
    [Column("void_date")] public DateTime? VoidDate { get; set; }
}

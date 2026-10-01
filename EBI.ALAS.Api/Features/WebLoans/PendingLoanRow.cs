using System.ComponentModel.DataAnnotations.Schema;
namespace EBI.ALAS.Api.Features.WebLoans;
public class PendingLoanRow
{
    [Column("bch")] public string BranchCode { get; set; } = string.Empty;
    [Column("acct_no")] public string AccountNo { get; set; } = string.Empty;
    [Column("loan_no")] public string LoanNo { get; set; } = string.Empty;
    [Column("principal")] public decimal? Principal { get; set; }
    [Column("granted_rate")] public decimal? GrantedRate { get; set; }
    [Column("total_amortization")] public int? TotalAmortization { get; set; }
    [Column("date_granted")] public DateTime? DateGranted { get; set; }
    [Column("date_maturity")] public DateTime? DateMaturity { get; set; }
    [Column("creation_type")] public byte? CreationType { get; set; }
    [Column("c_doc_stamp")] public decimal? CDocStamp { get; set; }
    [Column("creation_type_label")]
    public string CreationTypeLabel { get; set; } = "Unknown";
    [Column("total_term_days")]
    public int? TotalTermDays { get; set; }
    [Column("product_with_desc")]
    public string? ProductWithDescription { get; set; }
    [Column("loan_purpose")] public string? LoanPurpose { get; set; }
    [Column("nthp")] public string? Nthp { get; set; }
    [Column("nthp_date")] public DateTime? NthpDate { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
namespace EBI.ALAS.Api.Features.Loans;
[Table("LoanProductChecklist")]
public class LoanProductChecklist
{
    [Column("LoanProduct")]
    public string LoanProduct { get; set; } = string.Empty;
    [Column("IdCode")]
    public string IdCode { get; set; } = string.Empty;
    public LoanProduct? Product { get; set; }
}

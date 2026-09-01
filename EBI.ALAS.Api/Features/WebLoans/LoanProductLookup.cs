using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;
[Table("loan_product", Schema = "dbo")]
public class LoanProductLookup
{
    [Column("id_code")] public string IdCode { get; set; } = string.Empty;
    [Column("description")] public string Description { get; set; } = string.Empty;
}

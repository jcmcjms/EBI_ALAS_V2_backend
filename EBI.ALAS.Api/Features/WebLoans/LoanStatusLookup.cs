using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;
[Table("loan_status", Schema = "dbo")]
public class LoanStatusLookup
{
    [Column("id_code")] public string IdCode { get; set; } = string.Empty;
    [Column("description")] public string Description { get; set; } = string.Empty;
}

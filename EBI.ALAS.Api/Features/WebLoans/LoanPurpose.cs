using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;
[Table("loan_purpose", Schema = "dbo")]
public class LoanPurpose
{
    [Column("path")] public string Path { get; set; } = string.Empty;
    [Column("description")] public string Description { get; set; } = string.Empty;
}
using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;

/// <summary>
/// Maps to dbo.loan_product in the WebLoan database — loan product lookup
/// (e.g. A01 "APDS 1YR DIMINISHING").
/// </summary>
[Table("loan_product", Schema = "dbo")]
public class LoanProductLookup
{
    [Column("id_code")] public string IdCode { get; set; } = string.Empty;
    [Column("description")] public string Description { get; set; } = string.Empty;
}

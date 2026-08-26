using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;

/// <summary>
/// Maps to dbo.loan_status in the WebLoan database — loan status code lookup.
/// Codes observed on server: 01 Current, 02 PastDue Performing, 03 PastDue Non-Performing,
/// 04 Litigation, 05 Total TPL, 06 Write-Off, 07 Total, 08 Transfer Asset, 09 Grand Total.
/// </summary>
[Table("loan_status", Schema = "dbo")]
public class LoanStatusLookup
{
    [Column("id_code")] public string IdCode { get; set; } = string.Empty;
    [Column("description")] public string Description { get; set; } = string.Empty;
}

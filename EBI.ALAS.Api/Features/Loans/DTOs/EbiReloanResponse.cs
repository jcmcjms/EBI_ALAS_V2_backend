namespace EBI.ALAS.Api.Features.Loans.DTOs;

/// <summary>
/// Response DTO for EBI reloan details.
/// </summary>
public class EbiReloanResponse
{
    public int Id { get; set; }
    public string Pn { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal ExistingDeduction { get; set; }
    public decimal OutstandingBalance { get; set; }
    public decimal PayToClose { get; set; }
}

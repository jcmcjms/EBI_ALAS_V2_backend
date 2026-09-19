namespace EBI.ALAS.Api.Features.Loans.DTOs;

/// <summary>
/// Response DTO for buy-out loan details.
/// </summary>
public class BuyOutResponse
{
    public int Id { get; set; }
    public string Pn { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amortization { get; set; }
    public decimal OutstandingBalance { get; set; }
}

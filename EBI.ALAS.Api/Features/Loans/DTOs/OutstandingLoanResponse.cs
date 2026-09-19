namespace EBI.ALAS.Api.Features.Loans.DTOs;

/// <summary>
/// Response DTO for outstanding loan details.
/// </summary>
public class OutstandingLoanResponse
{
    public int Id { get; set; }
    public string Pn { get; set; } = string.Empty;
    public decimal PrincipalBalance { get; set; }
    public decimal Amortization { get; set; }
    public decimal OutstandingBalance { get; set; }
    public DateOnly? DateGranted { get; set; }
    public DateOnly? DateMaturity { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProductWithDescription { get; set; }
}

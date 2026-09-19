namespace EBI.ALAS.Api.Features.Loans.DTOs;

/// <summary>
/// Response DTO for incoming loan details.
/// </summary>
public class IncomingLoanResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Deductions { get; set; }
    public string Remarks { get; set; } = string.Empty;
}

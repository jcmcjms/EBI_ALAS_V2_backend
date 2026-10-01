namespace EBI.ALAS.Api.Features.Loans;
public class BuyOut
{
    public int Id { get; set; }
    public int LoanApplicationId { get; set; }
    public string Pn { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amortization { get; set; }
    public decimal OutstandingBalance { get; set; }
    public LoanApplication LoanApplication { get; set; } = null!;
}

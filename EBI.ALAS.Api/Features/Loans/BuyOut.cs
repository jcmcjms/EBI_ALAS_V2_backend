namespace EBI.ALAS.Api.Features.Loans;
public class BuyOut
{
    public int Id { get; set; }
    public int LoanApplicationId { get; set; }
    public string CreditorName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal? MonthlyAmortization { get; set; }

    // Navigation Property
    public LoanApplication LoanApplication { get; set; } = null!;
}

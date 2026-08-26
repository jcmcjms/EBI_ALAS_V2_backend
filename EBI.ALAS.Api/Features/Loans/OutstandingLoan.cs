namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Outstanding loan entity for tracking existing loan obligations.
/// </summary>
public class OutstandingLoan
{
    public int Id { get; set; }
    public int LoanApplicationId { get; set; }
    public string CreditorName { get; set; } = string.Empty;
    public decimal? MonthlyPayment { get; set; }
    public decimal? Balance { get; set; }

    // Navigation Property
    public LoanApplication LoanApplication { get; set; } = null!;
}

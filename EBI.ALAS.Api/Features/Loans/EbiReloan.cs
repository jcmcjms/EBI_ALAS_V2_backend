namespace EBI.ALAS.Api.Features.Loans;

public class EbiReloan
{
    public int Id { get; set; }
    public int LoanApplicationId { get; set; }
    public string Pn { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal ExistingDeduction { get; set; }
    public decimal OutstandingBalance { get; set; }

    /// <summary>Amount the AO intends to settle on this reloan; ≤ OutstandingBalance (validated).</summary>
    public decimal PayToClose { get; set; }

    public LoanApplication LoanApplication { get; set; } = null!;
}
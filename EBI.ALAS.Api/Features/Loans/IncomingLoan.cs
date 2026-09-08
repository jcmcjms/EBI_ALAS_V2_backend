namespace EBI.ALAS.Api.Features.Loans;

public class IncomingLoan
{
    public int Id { get; set; }
    public int LoanApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Deductions { get; set; }
    public string Remarks { get; set; } = string.Empty;

    public LoanApplication LoanApplication { get; set; } = null!;
}
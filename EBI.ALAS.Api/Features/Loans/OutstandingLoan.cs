namespace EBI.ALAS.Api.Features.Loans;

public class OutstandingLoan
{
    public int Id { get; set; }
    public int LoanApplicationId { get; set; }
    public string Pn { get; set; } = string.Empty;
    public decimal PrincipalBalance { get; set; }
    public decimal Amortization { get; set; }
    public decimal OutstandingBalance { get; set; }
    public DateOnly? DateGranted { get; set; }
    public DateOnly? DateMaturity { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>Pre-joined "&lt;code&gt; - &lt;description&gt;" from webloan, for the approval sheet.</summary>
    public string? ProductWithDescription { get; set; }

    public LoanApplication LoanApplication { get; set; } = null!;
}
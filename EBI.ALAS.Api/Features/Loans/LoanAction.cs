using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Features.Loans;
public class LoanAction
{
    public int Id { get; set; }
    public int LoanApplicationId { get; set; }
    public int ActionByUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? Comments { get; set; }
    public DateTime ActionDate { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public LoanApplication LoanApplication { get; set; } = null!;
    public User ActionByUser { get; set; } = null!;
}

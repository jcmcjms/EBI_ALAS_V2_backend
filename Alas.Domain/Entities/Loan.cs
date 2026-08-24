namespace Alas.Domain.Entities;

public class Loan
{
    public Guid Id { get; set; }
    public string LoanNumber { get; set; } = string.Empty;
    public string BorrowerName { get; set; } = string.Empty;
    public string? BorrowerContact { get; set; }
    public decimal PrincipalAmount { get; set; }
    public decimal InterestRate { get; set; }
    public int TermMonths { get; set; }
    public string? Purpose { get; set; }
    public string? BranchId { get; set; }
    public LoanStatus Status { get; set; } = LoanStatus.Draft;
    public Guid CreatedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? ApprovedUtc { get; set; }
    public DateTimeOffset? DisbursedUtc { get; set; }
    public string? Remarks { get; set; }
    public string? RejectionReason { get; set; }
}

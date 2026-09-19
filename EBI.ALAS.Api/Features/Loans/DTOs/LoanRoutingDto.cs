namespace EBI.ALAS.Api.Features.Loans.DTOs;

/// <summary>
/// Response DTO for loan routing information.
/// Contains delegation-of-authority routing details.
/// </summary>
public class LoanRoutingDto
{
    public int LoanId { get; set; }
    public int? RequiredApprovalTier { get; set; }
    public int DeviationSeverity { get; set; }
    public decimal TotalExposure { get; set; }
    public string LoanType { get; set; } = string.Empty;
    public string? MatchedRule { get; set; }
    public bool DocumentsComplete { get; set; }
    public List<string> MissingDocuments { get; set; } = new();
    public int? AssignedApproverId { get; set; }
    public string? AssignedApproverName { get; set; }
}

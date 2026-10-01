namespace EBI.ALAS.Api.Features.Loans.DTOs;
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
    public int? EscalatedFromTier { get; set; }
    public string? NoAuthorityReason { get; set; }
    public RoutingEvaluatedInputsDto? Evaluated { get; set; }
}
public class RoutingEvaluatedInputsDto
{
    public string Cycle { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public decimal Exposure { get; set; }
}

namespace EBI.ALAS.Api.Features.Loans.DTOs;

/// <summary>
/// Response DTO for loan routing information.
/// Contains delegation-of-authority routing details with escalation metadata.
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

    /// <summary>The tier that originally matched but had no configured
    /// approver — escalation occurred from this tier. Null when no escalation.</summary>
    public int? EscalatedFromTier { get; set; }

    /// <summary>Reason routing failed. Null when routing succeeded.</summary>
    public string? NoAuthorityReason { get; set; }

    /// <summary>Evaluated routing inputs for audit/display.</summary>
    public RoutingEvaluatedInputsDto? Evaluated { get; set; }
}

public class RoutingEvaluatedInputsDto
{
    public string Cycle { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public decimal Exposure { get; set; }
}
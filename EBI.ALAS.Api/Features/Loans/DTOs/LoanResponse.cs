namespace EBI.ALAS.Api.Features.Loans.DTOs;
public sealed record DocumentFlagDto(
    DateTime FlaggedAt,
    int? FlaggedById,
    string? Reason,
    int MissingCount);
public class LoanResponse
{
    public int Id { get; set; }
    public string LamId { get; set; } = string.Empty;
    public string ApplicationGroupNo { get; set; } = string.Empty;
    public string BranchCode { get; set; } = string.Empty;
    public string LoanNo { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public int? CreationTypeCode { get; set; }
    public string? CreationTypeLabel { get; set; }
    public string? RequestingOfficer { get; set; }
    public string? Lai { get; set; }
    public string? CisId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string? Suffix { get; set; }
    public DateOnly? Birthdate { get; set; }
    public string? Address { get; set; }
    public string? Agency { get; set; }
    public string? Position { get; set; }
    public string? EmployeeId { get; set; }
    public decimal? NetTakeHomePay { get; set; }
    public string? LengthOfService { get; set; }
    public string? Region { get; set; }
    public string? DivisionCode { get; set; }
    public string? StationCode { get; set; }
    public string? MisAgency { get; set; }
    public string? School { get; set; }
    public string? Referrer { get; set; }
    public string? Purpose { get; set; }
    public decimal ProposedAmount { get; set; }
    public int TermDays { get; set; }
    public decimal InterestRate { get; set; }
    public DateOnly? NthpDate { get; set; }
    public int? PolicyTermMonths { get; set; }
    public int? ApprovalTermDays { get; set; }
    public decimal? AnnualRatePercent { get; set; }
    public decimal? CDocStamp { get; set; }
    public decimal NotarialFee { get; set; }
    public decimal DocStamps { get; set; }
    public decimal Insurance { get; set; }
    public decimal StandardNotarialFee { get; set; }
    public decimal StandardDocStamps { get; set; }
    public decimal StandardInsurance { get; set; }
    public decimal StandardApplicationCharge { get; set; }
    public decimal StandardAdvanceInterest { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal DeductionRate { get; set; }
    public decimal GrossProceeds { get; set; }
    public decimal NetProceedsOnDS { get; set; }
    public decimal NetProceedsToClient { get; set; }
    public decimal TotalExposure { get; set; }
    public decimal? MonthlyAmortization { get; set; }
    public decimal NetPayAfterDeduction { get; set; }
    public decimal GrossDisposableIncome { get; set; }
    public decimal CapacityDeductions { get; set; }
    public decimal NetDisposableIncome { get; set; }
    public decimal MaximumLoanableAmount { get; set; }
    public bool AmortizationExceedsDisposable { get; set; }
    public bool NthpBelowMinimum { get; set; }
    public string? VerificationFindings { get; set; }
    public bool HasDeviations { get; set; }
    public List<string> DeviationDetails { get; set; } = new();
    public Dictionary<string, string> DeviationJustifications { get; set; } = new();
    public string? Remarks { get; set; }
    public string? AoRecommendation { get; set; }
    public string? OtherRemarks { get; set; }
    public string? FeeDeviationJustification { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime ApplicationDate { get; set; }
    public DateTime LastActionDate { get; set; }
    public int CreatedById { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public List<LoanActionResponse> Actions { get; set; } = new();
    public string? LastActionByName { get; set; }
    public string? LastAction { get; set; }
    public string? EvaluationVerdict { get; set; }
    public string LoanType { get; set; } = "New";
    public int DeviationSeverity { get; set; }
    public int? RequiredApprovalTier { get; set; }
    public int? AssignedApproverId { get; set; }
    public string? AssignedApproverName { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime? DocumentsCompleteAt { get; set; }
    public int? MatchedButUnstaffedTier { get; set; }
    public string? NoAuthorityReason { get; set; }
    public DocumentFlagDto? DocumentFlag { get; set; }
    public List<OutstandingLoanResponse> OutstandingLoans { get; set; } = new();
    public List<BuyOutResponse> BuyOuts { get; set; } = new();
    public List<EbiReloanResponse> EbiReloans { get; set; } = new();
    public List<IncomingLoanResponse> IncomingLoans { get; set; } = new();
    public string? WebLoanCisNo { get; set; }
    public string? WebLoanBranchCode { get; set; }
    public List<string> WebLoanAccountNumbers { get; set; } = new();
    public List<string> WebLoanPnNumbers { get; set; } = new();
    public DateTime? WebLoanLastSyncedAt { get; set; }
    public int? PreLoanId { get; set; }
    public string? PreLoanFormNumber { get; set; }
}

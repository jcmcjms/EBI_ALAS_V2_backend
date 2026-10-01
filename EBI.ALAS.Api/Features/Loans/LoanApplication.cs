using EBI.ALAS.Api.Features.Auth;
namespace EBI.ALAS.Api.Features.Loans;
public sealed class LoanApplication
{
    public int Id { get; init; }
    public string LamId { get; set; } = string.Empty;
    public string ApplicationGroupNo { get; set; } = string.Empty;
    public string BranchCode { get; set; } = string.Empty;
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
    public string LoanNo { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public decimal ProposedAmount { get; set; }
    public int TermDays { get; set; }
    public decimal InterestRate { get; set; }
    public int? PolicyTermMonths { get; set; }
    public int? ApprovalTermDays { get; set; }
    public decimal? AnnualRatePercent { get; set; }
    public decimal? CDocStamp { get; set; }
    public DateOnly? NthpDate { get; set; }
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
    public List<string> DeviationDetails { get; set; } = [];
    public Dictionary<string, string> DeviationJustifications { get; set; } = [];
    public string? Remarks { get; set; }
    public string? AoRecommendation { get; set; }
    public string? OtherRemarks { get; set; }
    public string? FeeDeviationJustification { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime ApplicationDate { get; init; } = DateTime.UtcNow;
    public DateTime LastActionDate { get; set; } = DateTime.UtcNow;
    public string LoanType { get; set; } = "New";
    public EBI.ALAS.Api.Features.ApprovalMatrix.DeviationSeverity DeviationSeverity { get; set; }
    public int? RequiredApprovalTier { get; set; }
    public int? AssignedApproverId { get; set; }
    public User? AssignedApprover { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime? DocumentsCompleteAt { get; set; }
    public int? MatchedButUnstaffedTier { get; set; }
    public string? NoAuthorityReason { get; set; }
    public DateTime? DocumentsFlaggedAt { get; set; }
    public int? DocumentsFlaggedById { get; set; }
    public User? DocumentsFlaggedBy { get; set; }
    public string? DocumentFlagReason { get; set; }
    public int CreatedById { get; init; }
    public User CreatedBy { get; set; } = null!;
    public string? WebLoanCisNo { get; set; }
    public string? WebLoanBranchCode { get; set; }
    public List<string> WebLoanAccountNumbers { get; set; } = [];
    public List<string> WebLoanPnNumbers { get; set; } = [];
    public DateTime? WebLoanLastSyncedAt { get; set; }
    public int? PreLoanId { get; set; }
    public string? PreLoanFormNumber { get; set; }
    public ICollection<LoanAction> Actions { get; set; } = [];
    public ICollection<OutstandingLoan> OutstandingLoans { get; set; } = [];
    public ICollection<BuyOut> BuyOuts { get; set; } = [];
    public ICollection<EbiReloan> EbiReloans { get; set; } = [];
    public ICollection<IncomingLoan> IncomingLoans { get; set; } = [];
    public ICollection<LoanDeviation> Deviations { get; set; } = [];
    public ICollection<DocumentChecklist> DocumentChecklists { get; set; } = [];
}

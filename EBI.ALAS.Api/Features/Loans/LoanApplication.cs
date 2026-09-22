using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// One loan within a submitted application group. A single wizard submission
/// with N selected preloan PNs produces N rows sharing ApplicationGroupNo;
/// each row owns its own LAM LamId, workflow state and audit trail.
/// </summary>
public sealed class LoanApplication
{
    public int Id { get; init; }

    /// <summary>LAM identifier, e.g. LAM-20260908-000042. Unique.</summary>
    public string LamId { get; set; } = string.Empty;

    /// <summary>Groups the N loans created by one submission, e.g. APP-20260908-000017.</summary>
    public string ApplicationGroupNo { get; set; } = string.Empty;

    /// <summary>Acting officer's branch (JWT-derived, never client-supplied).</summary>
    public string BranchCode { get; set; } = string.Empty;

    // ── Branch & type (wizard §1.2) ────────────────────────────────
    public int? CreationTypeCode { get; set; }
    public string? CreationTypeLabel { get; set; }
    public string? RequestingOfficer { get; set; }
    public string? Lai { get; set; }

    // ── Client snapshot (§1.1 / §2, CIS-sourced) ───────────────────
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

    // ── Per-loan parameters (§3) ───────────────────────────────────
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
    public DateOnly? NthpDate { get; set; }

    // Bank fees
    public decimal NotarialFee { get; set; }
    public decimal DocStamps { get; set; }
    public decimal Insurance { get; set; }
    public decimal StandardNotarialFee { get; set; }
    public decimal StandardDocStamps { get; set; }
    public decimal StandardInsurance { get; set; }
    public decimal StandardApplicationCharge { get; set; }
    public decimal StandardAdvanceInterest { get; set; }

    // ── Computed snapshot (server-authoritative, never client-supplied) ──
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

    // ── Verification & deviations (§6 / §7) ────────────────────────
    public string? VerificationFindings { get; set; }
    public bool HasDeviations { get; set; }
    public List<string> DeviationDetails { get; set; } = [];
    public Dictionary<string, string> DeviationJustifications { get; set; } = [];
    public string? Remarks { get; set; }
    public string? AoRecommendation { get; set; }
    public string? OtherRemarks { get; set; }
    public string? FeeDeviationJustification { get; set; }

    // ── Status & dates ─────────────────────────────────────────────
    public string Status { get; set; } = "Draft";
    public DateTime ApplicationDate { get; init; } = DateTime.UtcNow;
    public DateTime LastActionDate { get; set; } = DateTime.UtcNow;

    // ── Delegation-of-authority routing ─────────────────────────────
    public string LoanType { get; set; } = "New";
    public EBI.ALAS.Api.Features.ApprovalMatrix.DeviationSeverity DeviationSeverity { get; set; }
    public int? RequiredApprovalTier { get; set; }
    public int? AssignedApproverId { get; set; }
    public User? AssignedApprover { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime? DocumentsCompleteAt { get; set; }

    /// <summary>
    /// Remembers which review desk the loan was held from so the automatic
    /// release returns it to the correct queue. Set by DocumentGateService
    /// on entry; cleared on release. Null for loans that were never held.
    /// </summary>
    public string? IncompleteReturnStatus { get; set; }

    // ── Audit ──────────────────────────────────────────────────────
    public int CreatedById { get; init; }
    public User CreatedBy { get; set; } = null!;

    // ── WebLoan traceability (read-only legacy references) ─────────
    public string? WebLoanCisNo { get; set; }
    public string? WebLoanBranchCode { get; set; }
    public List<string> WebLoanAccountNumbers { get; set; } = [];
    public List<string> WebLoanPnNumbers { get; set; } = [];
    public DateTime? WebLoanLastSyncedAt { get; set; }
    public int? PreLoanId { get; set; }
    public string? PreLoanFormNumber { get; set; }

    // ── Navigation properties ──────────────────────────────────────
    public ICollection<LoanAction> Actions { get; set; } = [];
    public ICollection<OutstandingLoan> OutstandingLoans { get; set; } = [];
    public ICollection<BuyOut> BuyOuts { get; set; } = [];
    public ICollection<EbiReloan> EbiReloans { get; set; } = [];
    public ICollection<IncomingLoan> IncomingLoans { get; set; } = [];
    public ICollection<LoanDeviation> Deviations { get; set; } = [];
    public ICollection<DocumentChecklist> DocumentChecklists { get; set; } = [];
}

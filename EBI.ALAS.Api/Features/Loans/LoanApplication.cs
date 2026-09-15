using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// One loan within a submitted application group. A single wizard submission
/// with N selected preloan PNs produces N rows sharing ApplicationGroupNo;
/// each row owns its own LAM LamId, workflow state and audit trail.
/// Client / obligation / verification data is snapshotted per row so every
/// loan file is a self-contained audit unit (mirrors the printed per-loan
/// approval sheet).
/// </summary>
public class LoanApplication
{
    public int Id { get; set; }

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

    /// <summary>Loan Application Index (account), e.g. 011-05-13081-1.</summary>
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

    // Manual-entry fields (not CIS-sourced)
    public string? School { get; set; }
    public string? Referrer { get; set; }

    // ── Per-loan parameters (§3) ───────────────────────────────────
    /// <summary>Preloan PN this application was encoded against.</summary>
    public string LoanNo { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public decimal ProposedAmount { get; set; }
    public int TermDays { get; set; }

    /// <summary>Per-annum rate. decimal(9,6): decimal(5,2) silently rounded 0.0966 → 0.10.</summary>
    public decimal InterestRate { get; set; }
    public DateOnly? NthpDate { get; set; }

    // Bank fees — AO entry plus the policy snapshot at encode time so
    // Compliance can diff overrides without re-deriving policy history.
    public decimal NotarialFee { get; set; }
    public decimal DocStamps { get; set; }
    public decimal Insurance { get; set; }
    public decimal StandardNotarialFee { get; set; }
    public decimal StandardDocStamps { get; set; }
    public decimal StandardInsurance { get; set; }
    public decimal StandardApplicationCharge { get; set; }
    public decimal StandardAdvanceInterest { get; set; }

    // ── Computed snapshot (server-authoritative, never client-supplied) ──
    // These columns are written from LoanComputationService.ComputeLoanMetrics
    // at submission time. The frontend recomputes them for preview/gating,
    // but the backend is the source of truth for the persisted record.
    // A tampered payload cannot inject ledger values because these are
    // always overwritten server-side.

    /// <summary>Sum of all upfront deductions (app charge + doc stamp + notarial + insurance + advance interest).</summary>
    public decimal TotalDeductions { get; set; }

    /// <summary>Total deductions as a fraction of proposed amount (e.g. 0.06 = 6%).</summary>
    public decimal DeductionRate { get; set; }

    /// <summary>Proposed amount minus total deductions.</summary>
    public decimal GrossProceeds { get; set; }

    /// <summary>Gross proceeds minus EBI reloan outstanding balances.</summary>
    public decimal NetProceedsOnDS { get; set; }

    /// <summary>Net proceeds on DS minus buy-out outstanding balances.</summary>
    public decimal NetProceedsToClient { get; set; }

    /// <summary>Proposed amount plus sum of outstanding loan principal balances.</summary>
    public decimal TotalExposure { get; set; }

    /// <summary>Monthly amortization computed from the annuity formula (DIM) or max(DIM, tiered minimum) (MIC).</summary>
    public decimal? MonthlyAmortization { get; set; }

    /// <summary>NTHP minus monthly amortization plus released deductions from reloans/buyouts.</summary>
    public decimal NetPayAfterDeduction { get; set; }

    /// <summary>NTHP plus released deductions from reloans/buyouts (gross).</summary>
    public decimal GrossDisposableIncome { get; set; }

    /// <summary>Minimum NTHP plus sum of incoming loan deductions.</summary>
    public decimal CapacityDeductions { get; set; }

    /// <summary>Gross disposable income minus capacity deductions.</summary>
    public decimal NetDisposableIncome { get; set; }

    /// <summary>Maximum loanable amount: netDisposable / annuityFactor. Closed-form O(1).</summary>
    public decimal MaximumLoanableAmount { get; set; }

    /// <summary>True when monthly amortization exceeds net disposable income (capacity gate failed).</summary>
    public bool AmortizationExceedsDisposable { get; set; }

    /// <summary>True when NTHP is below the required minimum.</summary>
    public bool NthpBelowMinimum { get; set; }

    // ── Verification & deviations (§6 / §7) ────────────────────────
    public string? VerificationFindings { get; set; }
    public bool HasDeviations { get; set; }
    public List<string> DeviationDetails { get; set; } = new();
    public Dictionary<string, string> DeviationJustifications { get; set; } = new();
    public string? Remarks { get; set; }
    public string? AoRecommendation { get; set; }
    public string? OtherRemarks { get; set; }
    public string? FeeDeviationJustification { get; set; }

    // ── Status & dates ─────────────────────────────────────────────
    public string Status { get; set; } = "Draft";
    public DateTime ApplicationDate { get; set; } = DateTime.UtcNow;
    public DateTime LastActionDate { get; set; } = DateTime.UtcNow;

    // ── Audit ──────────────────────────────────────────────────────
    public int CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;

    // ── WebLoan traceability (read-only legacy references) ─────────
    public string? WebLoanCisNo { get; set; }
    public string? WebLoanBranchCode { get; set; }
    public List<string> WebLoanAccountNumbers { get; set; } = new();
    public List<string> WebLoanPnNumbers { get; set; } = new();
    public DateTime? WebLoanLastSyncedAt { get; set; }
    public int? PreLoanId { get; set; }
    public string? PreLoanFormNumber { get; set; }

    // ── Navigation properties ──────────────────────────────────────
    public ICollection<LoanAction> Actions { get; set; } = new List<LoanAction>();
    public ICollection<OutstandingLoan> OutstandingLoans { get; set; } = new List<OutstandingLoan>();
    public ICollection<BuyOut> BuyOuts { get; set; } = new List<BuyOut>();
    public ICollection<EbiReloan> EbiReloans { get; set; } = new List<EbiReloan>();
    public ICollection<IncomingLoan> IncomingLoans { get; set; } = new List<IncomingLoan>();
    public ICollection<LoanDeviation> Deviations { get; set; } = new List<LoanDeviation>();
}
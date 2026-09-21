namespace EBI.ALAS.Api.Features.Loans;

// ─── POST /api/loans request envelope ─────────────────────────────────────
// Mirrors src/pages/loans/create/schema.ts field-for-field so the two never
// drift. Branch code and requesting officer are NOT part of the trust
// boundary: both are derived from the JWT / Users table server-side.

public sealed record SubmitLoanApplicationRequest
{
    public required BranchTypeSection BranchType { get; init; }
    public required ClientSection Client { get; init; }
    public required IReadOnlyList<LoanSection> Loans { get; init; }

    // Borrower-level snapshot of the legacy WebLoan portfolio — identical
    // for every loan in the group, and the source side of Outstanding ↔ EBI transfers.
    public IReadOnlyList<OutstandingLoanSection> OutstandingLoans { get; init; } = [];

    public PreLoanRefSection? PreLoan { get; init; }
}

public sealed record BranchTypeSection
{
    public int? CreationTypeCode { get; init; }
    public string CreationTypeLabel { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;

    /// <summary>Requesting officer from webloan solicitor data (frontend-supplied).</summary>
    public string RequestingOfficer { get; init; } = string.Empty;
    public string? Lai { get; init; }
}

public sealed record ClientSection
{
    public string CisId { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string? MiddleName { get; init; }
    public string LastName { get; init; } = string.Empty;
    public string? Suffix { get; init; }
    public string? Birthdate { get; init; }          // ISO-8601 date, parsed server-side
    public string? Address { get; init; }
    public string? Agency { get; init; }
    public string? Position { get; init; }
    public string? EmployeeId { get; init; }
    public decimal? NetTakeHomePay { get; init; }
    public string? LengthOfService { get; init; }
    public string? Region { get; init; }
    public string? DivisionCode { get; init; }
    public string? StationCode { get; init; }
    public string? MisAgency { get; init; }
    public string? School { get; init; }
    public string? Referrer { get; init; }
}

public sealed record LoanSection
{
    public string LoanNo { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string ProductDescription { get; init; } = string.Empty;
    public int? CreationTypeCode { get; init; }
    public string CreationTypeLabel { get; init; } = string.Empty;

    /// <summary>Must equal the acting officer's JWT branchId (server-asserted).</summary>
    public string BranchCode { get; init; } = string.Empty;
    public required LoanParametersSection Parameters { get; init; }

    // ── §5 obligations declared against THIS loan ──────────────────
    public IReadOnlyList<EbiReloanSection> EbiReloans { get; init; } = [];
    public IReadOnlyList<BuyOutSection> BuyOuts { get; init; } = [];
    public IReadOnlyList<IncomingLoanSection> IncomingLoans { get; init; } = [];

    // ── §6 / §7 per-loan audit trail ───────────────────────────────
    public required VerificationSection Verification { get; init; }
    public required DeviationsSection Deviations { get; init; }
}

public sealed record LoanParametersSection
{
    public string Product { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public decimal ProposedAmount { get; init; }
    public int Term { get; init; }
    public decimal InterestRate { get; init; }
    public string? NthpDate { get; init; }
    public decimal NotarialFee { get; init; }
    public decimal DocStamps { get; init; }
    public decimal Insurance { get; init; }
    public required FeeSnapshotSection StandardFeesSnapshot { get; init; }

    /// <summary>
    /// Amortization period count from webloan loan_data.total_amortization
    /// (e.g. 84 for monthly products). Used to derive the approval form's
    /// TERM (Days) via the 30-day-month convention. Null when unavailable.
    /// </summary>
    public int? PolicyTermMonths { get; init; }
}

public sealed record FeeSnapshotSection
{
    public decimal NotarialFee { get; init; }
    public decimal DocStamps { get; init; }
    public decimal Insurance { get; init; }
    public decimal ApplicationCharge { get; init; }
    public decimal AdvanceInterest { get; init; }
}

public sealed record OutstandingLoanSection
{
    public string Pn { get; init; } = string.Empty;
    public decimal PrincipalBalance { get; init; }
    public decimal Amortization { get; init; }
    public decimal OutstandingBalance { get; init; }
    public string? DateGranted { get; init; }
    public string? DateMaturity { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ProductWithDescription { get; init; }
}

public sealed record EbiReloanSection
{
    public string Pn { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal ExistingDeduction { get; init; }
    public decimal OutstandingBalance { get; init; }
    public decimal PayToClose { get; init; }
}

public sealed record BuyOutSection
{
    public string Pn { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal Amortization { get; init; }
    public decimal OutstandingBalance { get; init; }
}

public sealed record IncomingLoanSection
{
    public string Name { get; init; } = string.Empty;
    public decimal Deductions { get; init; }
    public string Remarks { get; init; } = string.Empty;
}

public sealed record PreLoanRefSection
{
    public int Id { get; init; }
    public string AccountNo { get; init; } = string.Empty;
    public string Bch { get; init; } = string.Empty;
    public string? FormNumber { get; init; }
    public string? ProductDescription { get; init; }
}

public sealed record VerificationSection
{
    public string Findings { get; init; } = string.Empty;
}

public sealed record DeviationsSection
{
    public bool HasDeviations { get; init; }
    public IReadOnlyList<string> DeviationDetails { get; init; } = [];
    public IReadOnlyDictionary<string, string> DeviationJustifications { get; init; }
        = new Dictionary<string, string>();
    public string? Remarks { get; init; }
    public string? AoRecommendation { get; init; }
    public string OtherRemarks { get; init; } = string.Empty;
    public string? FeeDeviationJustification { get; init; }
}

// ─── Response ─────────────────────────────────────────────────────────────

public sealed record LoanSubmissionResponse
{
    public required string ApplicationGroupNo { get; init; }
    public IReadOnlyList<CreatedLoan> Loans { get; init; } = [];
}

public sealed record CreatedLoan
{
    public int Id { get; init; }

    /// <summary>The generated LAM ID (LoanApplicationIndex).</summary>
    public string LamId { get; init; } = string.Empty;
    public string LoanNo { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public decimal ProposedAmount { get; init; }
    public string Status { get; init; } = string.Empty;

    // ── List-view enrichment (populated by GET /api/loans, null on POST) ──
    //
    // POST only knows what was just submitted; it doesn't read these fields
    // back from the DB. They're nullable so a POST response deserializes
    // cleanly on the FE without breaking the shared DTO shape. GET populates
    // them from LoanApplication rows so the monitoring/list table renders
    // client name + branch + loan type + product without a second call.

    /// <summary>Owning branch code (e.g. "011"). Null on POST responses.</summary>
    public string? BranchCode { get; init; }

    /// <summary>Product description (e.g. "Quick Loan"). Null on POST responses.</summary>
    public string? Product { get; init; }

    /// <summary>Creation type code (0/1/2/6). Null on POST responses.</summary>
    public int? CreationTypeCode { get; init; }

    /// <summary>Creation type label (e.g. "New Loan", "Renewal"). Null on POST responses.</summary>
    public string? CreationTypeLabel { get; init; }

    /// <summary>Borrower first name (CIS snapshot). Null on POST responses.</summary>
    public string? FirstName { get; init; }

    /// <summary>Borrower middle name. Null on POST responses.</summary>
    public string? MiddleName { get; init; }

    /// <summary>Borrower last name (CIS snapshot). Null on POST responses.</summary>
    public string? LastName { get; init; }

    /// <summary>Borrower suffix. Null on POST responses.</summary>
    public string? Suffix { get; init; }

    // ── Monitoring-table enrichment (populated by GET /api/loans) ─────────
    //
    // These fields are what the loan monitoring table renders:
    //   • ApplicationDate   → "App. Date" column
    //   • LastActionDate    → "Time Lapsed" computation + sorting
    //   • CreatedByName     → creator fallback when no audit actions exist
    //   • LastActionByName  → "Last Action By" column (from audit trail)
    //   • LastAction        → verb subtext (Created, PushedBack, …)
    //
    // They are nullable because POST /api/loans doesn't read them back —
    // it only knows what was just submitted. GET populates them from
    // LoanApplication + the CreatedBy navigation property.

    /// <summary>When the loan application was filed. Null on POST responses.</summary>
    public DateTime? ApplicationDate { get; init; }

    /// <summary>Last workflow action date (used for "time lapsed" calc). Null on POST responses.</summary>
    public DateTime? LastActionDate { get; init; }

    /// <summary>Full name of the officer who created the loan. Null on POST responses.</summary>
    public string? CreatedByName { get; init; }

    /// <summary>Officer the application last flowed through (Encoder →
    /// Recommender → Evaluator → Approver), resolved from the latest LoanAction.
    /// Falls back to the creator server-side when no action exists yet.
    /// Null on POST responses (nothing has happened since minting).</summary>
    public string? LastActionByName { get; init; }

    /// <summary>Verb of the latest workflow action (Created, StatusChanged,
    /// PushedBack, EvaluatedRecommended, EvaluatedNotRecommended…).</summary>
    public string? LastAction { get; init; }

    /// <summary>User ID of the encoder who created this application.
    /// Populated by GET /api/loans; null on POST responses.</summary>
    public int? CreatedById { get; init; }

    // ── Delegation-of-authority enrichment (GET /api/loans only) ─────
    // These fields mirror the detail endpoint (LoanResponse) so the
    // monitoring table can render Docs status + Assigned To without
    // a per-row subquery or a second API call.

    /// <summary>Derived boolean: true when DocumentsCompleteAt is set.
    /// Null on POST responses (not yet verified).</summary>
    public bool? DocumentsComplete { get; init; }

    /// <summary>Timestamp when document completeness was last verified.
    /// Null = unchecked/incomplete. Populated by GET /api/loans.</summary>
    public DateTime? DocumentsCompleteAt { get; init; }

    /// <summary>Frozen routing tier at entry to ForApproval.
    /// Null until routed. Populated by GET /api/loans.</summary>
    public int? RequiredApprovalTier { get; init; }

    /// <summary>User ID of the assigned approver (active lease).
    /// Null when unassigned. Populated by GET /api/loans.</summary>
    public int? AssignedApproverId { get; init; }

    /// <summary>Display name of the assigned approver.
    /// Null when unassigned. Populated by GET /api/loans.</summary>
    public string? AssignedApproverName { get; init; }

    // ── Workflow queue enrichment (GET /api/loans only) ─────────────
    // These fields drive the queue-aware "Assigned To" column and the
    // "My turn" filter in the monitoring table.

    /// <summary>Current review desk stage (Recommendation/Evaluation/Approval).
    /// Null when not in a review desk. Populated by GET /api/loans.</summary>
    public string? QueueStage { get; init; }

    /// <summary>Position in the queue (1 = on the desk right now).
    /// Null when not in a review desk. Populated by GET /api/loans.</summary>
    public int? QueuePosition { get; init; }

    /// <summary>Total items in this desk's queue.
    /// Null when not in a review desk. Populated by GET /api/loans.</summary>
    public int? QueueLength { get; init; }

    /// <summary>Display name of the head owner (who is reviewing).
    /// Null when not in a review desk or desk is unowned. Populated by GET /api/loans.</summary>
    public string? QueueOwnerName { get; init; }

    /// <summary>True when this loan is the head of its desk queue.
    /// Populated by GET /api/loans.</summary>
    public bool IsQueueHead { get; init; }
}

// ─── GET /api/loans/{id}/history — timeline entries ─────────────────────
// One row per LoanAction. ActionBy is the resolved server-side full name
// (not a user id) so the frontend never has to perform a second lookup.
// Nullable strings mirror LoanAction.FromStatus / ToStatus / Comments.
public sealed record LoanHistoryEntryResponse(
    int Id,
    string ActionBy,
    string Action,
    string? FromStatus,
    string? ToStatus,
    string? Comments,
    DateTime ActionDate,
    string ActionByRole
);
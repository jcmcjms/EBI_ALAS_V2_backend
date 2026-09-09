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
    public IReadOnlyList<OutstandingLoanSection> OutstandingLoans { get; init; } = [];
    public IReadOnlyList<EbiReloanSection> EbiReloans { get; init; } = [];
    public IReadOnlyList<BuyOutSection> BuyOuts { get; init; } = [];
    public IReadOnlyList<IncomingLoanSection> IncomingLoans { get; init; } = [];
    public PreLoanRefSection? PreLoan { get; init; }
    public required VerificationSection Verification { get; init; }
    public required DeviationsSection Deviations { get; init; }
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
}

public sealed record FeeSnapshotSection
{
    public decimal NotarialFee { get; init; }
    public decimal DocStamps { get; init; }
    public decimal Insurance { get; init; }
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
}
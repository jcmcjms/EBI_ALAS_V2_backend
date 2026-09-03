namespace EBI.ALAS.Api.Features.WebLoans;

// ─── Loan status ──────────────────────────────────────────────────────────
// Mirrors the SQL CASE block from the original webloan query. Translated
// here so the frontend gets human-readable labels without parsing integers.
public enum WebLoanStatus
{
    Current = 0,
    PastduePerforming = 1,
    PastdueNonPerforming = 2,
    LitigationOrITL = 3,
    TransferOfAsset = 4,
    WriteOff = 5,
    Unknown = 99
}

// ─── Region codes (cis_info.b_region_code) ────────────────────────────────
// The webloan column is varchar(20) and mixes numeric strings ("1".."18")
// with codes for non-regional groupings ("NCR", "CRG"). Mapping here so the
// API surface is consistent regardless of how webloan stores the value.
public static class WebLoanRegions
{
    public static string Resolve(string? code) => code switch
    {
        "1" => "Region 1",
        "2" => "Region 2",
        "3" => "Region 3",
        "4" => "Region 4",
        "5" => "Region 5",
        "6" => "Region 6",
        "7" => "Region 7",
        "8" => "Region 8",
        "9" => "Region 9",
        "10" => "Region 10",
        "11" => "Region 11",
        "12" => "Region 12",
        "13" => "Region 13",
        "14" => "Region 14",
        "15" => "Region 15",
        "16" => "Region 16",
        "17" => "Region 17",
        "18" => "Region 18",
        "NCR" => "NCR",
        "CRG" => "CARAGA",
        _ => "Unknown Region"
    };

    public static WebLoanStatus ResolveLoanStatus(byte? code) => code switch
    {
        0 => WebLoanStatus.Current,
        1 => WebLoanStatus.PastduePerforming,
        2 => WebLoanStatus.PastdueNonPerforming,
        3 => WebLoanStatus.LitigationOrITL,
        4 => WebLoanStatus.TransferOfAsset,
        5 => WebLoanStatus.WriteOff,
        _ => WebLoanStatus.Unknown
    };

    public static string Label(WebLoanStatus status) => status switch
    {
        WebLoanStatus.Current => "Current",
        WebLoanStatus.PastduePerforming => "Pastdue Performing",
        WebLoanStatus.PastdueNonPerforming => "Pastdue Non-Performing",
        WebLoanStatus.LitigationOrITL => "Litigation / ITL",
        WebLoanStatus.TransferOfAsset => "Transfer of Asset",
        WebLoanStatus.WriteOff => "Write-off",
        _ => "Unknown"
    };

    // Mirrors the `Case ld.creation_type` block in the original webloan
    // SQL. 0=New, 1=Reloan, 2=Restructured, 6=Additional Loan. Anything
    // else (including NULL when no loan_data row exists) → "Unknown".
    public static string CreationTypeLabel(byte? code) => code switch
    {
        0 => "New Loan",
        1 => "Reloan",
        2 => "Restructured",
        6 => "Additional Loan",
        _ => "Unknown"
    };
}

// ─── GET /api/webloans/cis/{cisNo}/search ─────────────────────────────────
public record CisSearchResponse(
    BorrowerDto Borrower,
    IReadOnlyList<AccountDto> Accounts);

public record BorrowerDto(
    string CisNo,
    string FirstName,
    string? MiddleName,
    string LastName,
    string? Title,
    string? Appelation,
    DateTime? BirthDate,
    string? Address,
    string? AgencyType,
    string? PositionTitle,
    string? Region,
    string? RegionCode,
    string? DivisionCode,
    string? StationCode,
    string? EmployeeNumber,
    string? MisAgency,
    string? RequestingOfficer,
    string? LengthOfService);   // "<years> years, <months> months" — sourced from
                                //   check_list_data WHERE check_list_item = 'CCR10'

public record AccountDto(
    string BankCode,
    string BranchCode,
    string AccountNo,
    string AccountId,           // combined "<branchCode>-<accountNo>" per WebLoanAccountId
    string? Name,
    decimal? CreditLimit,
    decimal? UsedCredit,
    string? BorrowerType);

// ─── GET /api/webloans/cis/{cisNo}/accounts/{accountId}/outstanding-loans ─
// accountId is the combined "<branchCode>-<accountNo>" form
// (e.g. "011-05-13081-1") — see WebLoanAccountId.
public record OutstandingLoansResponse(
    string CisNo,
    string AccountId,             // "<branchCode>-<accountNo>" — echoed from the URL
    string BranchCode,            // parsed from AccountId (mirror of bch column)
    string AccountNo,             // parsed from AccountId (mirror of acct_no column)
    IReadOnlyList<OutstandingLoanDto> Loans);

public record OutstandingLoanDto(
    string? LoanNo,
    decimal? Principal,
    decimal? PrincipalBalance,
    decimal? AmortAmount,     // CASE-computed: principal for C35/C23,
                             //   otherwise amort_data.total_amort (amort_no=1).
                             //   NULL when no amort_data row exists for
                             //   a non-C35/C23 loan.
    DateTime? DateGranted,
    DateTime? DateMaturity,
    string ProductCode,
    string ProductStatus,        // "<loan_product> - <status label>"
    // "<loan_product> - <description>" (e.g. "C35 - Quick Loan"), or
    // just the product code when no loan_product row matched the join
    // (orphaned/retired product). Assembled in SQL via a LEFT JOIN to
    // webloan.dbo.loan_product on (ld.loan_product = lp.id_code); see
    // WebLoanRepository.GetOutstandingLoansAsync for the join +
    // ISNULL(coalesce) rationale.
    string ProductWithDescription);

// ─── GET /api/webloans/cis/{cisNo}/accounts/{accountId}/pending-loan ──────
// accountId is the combined "<branchCode>-<accountNo>" form
// (e.g. "011-05-13081-1") — see WebLoanAccountId.
//
// Returns ALL in-flight pre_loan_data rows for the (bch, acct_no) pair
// + NTHP (Net Take-Home Pay) enrichment joined from check_list_data
// WHERE check_list_item = 'CCR07'. Used by underwriters while evaluating
// pending loan applications.
//
// Multiple in-flight loans are possible because the schema permits
// duplicates for (bch, acct_no) — e.g. an account with several
// preparation cycles in progress.
//
// NTHP is hoisted to the response level because it is a CIS-level
// attribute (joined on cis_no), not a loan-level one. Duplicating it
// per loan would mislead the UI into thinking NTHP differs by loan.
public record PendingLoanResponse(
    string CisNo,
    string AccountId,             // "<branchCode>-<accountNo>" — echoed from the URL
    string BranchCode,            // parsed from AccountId
    string AccountNo,             // parsed from AccountId
    IReadOnlyList<PendingLoanDto> Loans,
    string? Nthp,                 // Net Take-Home Pay amount (varchar number)
    DateTime? NthpDate);

public record PendingLoanDto(
    string LoanNo,
    decimal? Principal,
    decimal? GrantedRate,
    int? TotalTermDays,       // total_amortization * 30 (per original SQL)
    string ProductWithDescription,  // "<loan_product> - <description>"
    string? LoanPurpose,
    byte? CreationType,           // raw code from loan_data.creation_type
    string CreationTypeLabel);    // "New Loan" / "Reloan" / "Restructured" / "Additional Loan" / "Unknown"

// ─── Combined account identifier ("branchCode-accountNo") ─────────────────
//
// The two drill-down endpoints (outstanding-loans, pending-loan) take a
// single route parameter `accountId` instead of separate `branchCode` and
// `accountNo` query/path parameters — the branch becomes part of the
// account identity, mirroring how webloan itself stores it (bch + acct_no).
//
// Format: <branchCode>-<accountNo>  (e.g. "011-05-13081-1")
//
// Split rule: split on the FIRST '-' only. The remainder is treated as the
// literal account number verbatim, so account numbers that themselves
// contain hyphens ("05-13081-1") are preserved.
//
//   "011-05-13081-1"  →  bch="011",  acctNo="05-13081-1"
//   "011-05-13081-1-A" → bch="011",  acctNo="05-13081-1-A"
//
// Validation: both segments must be non-empty after trimming. The format
// is intentionally lenient on input characters because webloan's acct_no
// column is varchar and accepts a wide range of values in production data.
public static class WebLoanAccountId
{
    public static string Format(string branchCode, string accountNo)
        => $"{branchCode}-{accountNo}";

    // Returns (branchCode, accountNo). Throws ArgumentException when the
    // combined string is malformed — the endpoint maps that to 400 via
    // the existing GlobalExceptionHandler.
    public static (string BranchCode, string AccountNo) Parse(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new ArgumentException(
                "accountId is required and must be in '<branchCode>-<accountNo>' format.",
                nameof(accountId));
        }

        var idx = accountId.IndexOf('-');
        if (idx <= 0 || idx == accountId.Length - 1)
        {
            throw new ArgumentException(
                $"accountId '{accountId}' is malformed. Expected '<branchCode>-<accountNo>' " +
                $"(e.g. '011-05-13081-1').",
                nameof(accountId));
        }

        var bch = accountId[..idx].Trim();
        var acct = accountId[(idx + 1)..].Trim();

        if (bch.Length == 0 || acct.Length == 0)
        {
            throw new ArgumentException(
                $"accountId '{accountId}' has an empty branch or account segment.",
                nameof(accountId));
        }

        return (bch, acct);
    }
}
namespace EBI.ALAS.Api.Features.WebLoans;

/// <summary>
/// Full borrower profile assembled from the WebLoan database for a given CIS number.
/// Structured to mirror the ALAS loan application form sections.
/// </summary>
public class WebLoanBorrowerResponse
{
    public BranchAndTypeSection BranchAndType { get; set; } = new();
    public PersonalInformationSection PersonalInformation { get; set; } = new();
    public OptionalInformationSection OptionalInformation { get; set; } = new();
    public LoanInformationSection LoanInformation { get; set; } = new();
    public DeviationSection Deviation { get; set; } = new();
    public List<OutstandingLoanItem> OutstandingLoans { get; set; } = new();
    public List<EbiReloanAccountItem> EbiReloanAccounts { get; set; } = new();
    public List<BuyOutAccountItem> BuyOutAccounts { get; set; } = new();
    public List<IncomingLoanItem> IncomingLoans { get; set; } = new();
}

/// <summary>
/// Branch &amp; Type section — identifies where the borrower belongs and their loan accounts (LAI).
/// </summary>
public class BranchAndTypeSection
{
    /// <summary>
    /// Loan type label from loan_data.creation_type of the borrower's most recent
    /// active loan (e.g. "Reloan", "New Loan"). Display this in the frontend.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>Raw creation_type code for frontend logic/filtering (0=New, 1=Reloan, ...).</summary>
    public int? TypeCode { get; set; }

    /// <summary>Branch code (webloan bch) of the client's home branch.</summary>
    public string? BranchCode { get; set; }

    /// <summary>
    /// Requesting officer. Sourced from <c>loan_acct_info.solicitor</c> resolved
    /// against <c>dbo.mis_group.path</c> where <c>group_no = 2</c> — the
    /// description of the resolved row is the officer's full name
    /// (e.g. "ALDREX JOEY L. CEZAR"). Populated by <see cref="WebLoanService"/>
    /// from the most recent account owned by the borrower.
    /// </summary>
    public string? RequestingOfficer { get; set; }

    /// <summary>Client Information System number.</summary>
    public string? CisNo { get; set; }

    /// <summary>Loan Account Info numbers (acct_no) owned by this client.</summary>
    public List<string> Lai { get; set; } = new();
}

/// <summary>Personal Information section — sourced from cis_info.</summary>
public class PersonalInformationSection
{
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    /// <summary>Suffix (Jr/Sr/III). Mapped from cis_info.appelation — confirm with webloan team whether title vs appelation holds the suffix.</summary>
    public string? Suffix { get; set; }
    public DateTime? Birthdate { get; set; }
    public string? Address { get; set; }

    /// <summary>Company / agency name (cis_info.b_comp).</summary>
    public string? AgencyName { get; set; }

    /// <summary>
    /// Agency type raw code (cis_info.company_type tinyint) — kept for backward
    /// compatibility with callers that consume the raw byte.
    /// </summary>
    public byte? AgencyTypeCode { get; set; }

    /// <summary>
    /// Agency type description resolved from <c>cis_info_misc_data</c> (id_code=14)
    /// joined to <c>mis_group.id_code</c> in <c>group_no = 26</c> (e.g. "RPSU",
    /// "GOVERNMENT"). This is the human-readable agency classification the form
    /// actually displays next to the agency name.
    /// </summary>
    public string? AgencyType { get; set; }

    /// <summary>Position / title (cis_info.b_jtitle).</summary>
    public string? PositionTitle { get; set; }

    /// <summary>
    /// Length of service. NOT stored in the identified webloan tables
    /// (cis_info / loan_acct_info / pre_loan_data / loan_data) — populate in ALAS or extend source later.
    /// </summary>
    public string? LengthOfService => null;

    public string? RegionCode { get; set; }
    public string? DivisionCode { get; set; }
    public string? StationCode { get; set; }
    public string? EmployeeNo { get; set; }

    /// <summary>
    /// Primary MIS Agency path from <c>loan_acct_info.cat_mis_group</c>
    /// (e.g. "INDIV/SAL"). This is the path itself; the human-readable
    /// agency name is <see cref="MisAgencyName"/>.
    /// </summary>
    public string? MisAgency { get; set; }

    /// <summary>
    /// Resolved secondary MIS Agency description from <c>loan_acct_info.cat_mis_group2</c>
    /// joined to <c>mis_group.path</c> (e.g. "DEPED LIANGA"). The agency
    /// the borrower is associated with for reporting/segmentation purposes.
    /// </summary>
    public string? MisAgencyName { get; set; }
}

/// <summary>Optional Information section — referrer/school are not stored in the identified webloan tables.</summary>
public class OptionalInformationSection
{
    public string? Referrer => null;
    public string? School => null;
}

/// <summary>Loan Information section — sourced from loan_data (most recent active loan drives the summary).</summary>
public class LoanInformationSection
{
    public string? ProductCode { get; set; }
    public string? ProductDescription { get; set; }

    /// <summary>Total amortization count (term).</summary>
    public int? TermMonths { get; set; }

    /// <summary>Payment interval in months.</summary>
    public int? PaymentIntervalMonths { get; set; }

    /// <summary>Interest rate granted on the loan.</summary>
    public decimal? InterestRate { get; set; }

    public string? Purpose { get; set; }

    /// <summary>Proposed amount — applied principal of the latest loan record.</summary>
    public decimal? ProposedAmount { get; set; }

    /// <summary>Net take-home pay. Captured in ALAS at application time; not stored in webloan.</summary>
    public decimal? Nthp => null;

    public DateTime? NthpDate => null;
}

/// <summary>
/// Deviation section — deviations are assessed within ALAS during evaluation;
/// no deviation data exists in the webloan tables.
/// </summary>
public class DeviationSection
{
    public bool HasDeviations => false;
    public List<string> Deviations { get; set; } = new();
}

/// <summary>Outstanding Loans row (excludes closed/payoff and write-off accounts).</summary>
public class OutstandingLoanItem
{
    /// <summary>Promissory Note number (loan_data.loan_no).</summary>
    public string Pn { get; set; } = string.Empty;
    public string? AccountNo { get; set; }
    public decimal? PrincipalBalance { get; set; }
    public decimal? Amortization { get; set; }
    public decimal? OutstandingBalance { get; set; }
    public DateTime? DateGranted { get; set; }
    public DateTime? DateMaturity { get; set; }
    public string? Status { get; set; }
}

/// <summary>EBI account considered for reloan (existing EBI loan accounts of the borrower).</summary>
public class EbiReloanAccountItem
{
    public string Pn { get; set; } = string.Empty;
    public string? Name { get; set; }

    /// <summary>Computed by ALAS at evaluation time; not stored in webloan.</summary>
    public decimal? ExistingDeductions => null;

    /// <summary>Computed by ALAS at evaluation time; not stored in webloan.</summary>
    public decimal? PayToClose => null;

    public decimal? PrincipalBalance { get; set; }
    public string? Status { get; set; }
}

/// <summary>
/// Buy-out accounts from other financial institutions are external to webloan
/// (no per-account buy-out table exists there) — collected in the ALAS form.
/// Returned empty for schema compatibility.
/// </summary>
public class BuyOutAccountItem
{
    public string Pn { get; set; } = string.Empty;
    public string? Name { get; set; }
    public decimal? Amortization { get; set; }
    public decimal? OutstandingBalance { get; set; }
}

/// <summary>
/// Incoming/undeducted loans keyed through cis_no — no dedicated source table was
/// identified in webloan; returned empty until the webloan team confirms the source.
/// </summary>
public class IncomingLoanItem
{
    public string? Name { get; set; }
    public decimal? Deductions { get; set; }
    public string? Remarks { get; set; }
}

// ============================================================================
// STEP-BY-STEP SEARCH DTOs
// ============================================================================

/// <summary>
/// Step 1: CIS Search Result — basic borrower info + list of accounts for selection.
/// </summary>
public class CisSearchResult
{
    /// <summary>Client Information System number.</summary>
    public string CisNo { get; set; } = string.Empty;

    /// <summary>Full name for display.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Branch code (home branch).</summary>
    public string BranchCode { get; set; } = string.Empty;

    /// <summary>Loan accounts owned by this borrower (from loan_acct_info).</summary>
    public List<CisAccountSummary> Accounts { get; set; } = new();
}

/// <summary>
/// Account summary for CIS search results — minimal info for account selection UI.
/// </summary>
public class CisAccountSummary
{
    /// <summary>Account number (acct_no).</summary>
    public string AccountNo { get; set; } = string.Empty;

    /// <summary>Account name (acct_name).</summary>
    public string? AccountName { get; set; }

    /// <summary>Account address (acct_address).</summary>
    public string? AccountAddress { get; set; }

    /// <summary>MIS Group (cat_mis_group).</summary>
    public string? MisGroup { get; set; }

    /// <summary>Number of PN records associated with this account.</summary>
    public int PnCount { get; set; }
}

/// <summary>
/// Step 2: Account Detail with PN records — detailed view after account selection.
/// </summary>
public class AccountWithPnsResponse
{
    /// <summary>Account number (acct_no).</summary>
    public string AccountNo { get; set; } = string.Empty;

    /// <summary>Account name (acct_name).</summary>
    public string? AccountName { get; set; }

    /// <summary>Account address (acct_address).</summary>
    public string? AccountAddress { get; set; }

    /// <summary>MIS Group (cat_mis_group).</summary>
    public string? MisGroup { get; set; }

    /// <summary>All PN records for this account from loan_data.</summary>
    public List<PnRecord> PnRecords { get; set; } = new();
}

/// <summary>
/// PN (Promissory Note) record from loan_data table.
/// </summary>
public class PnRecord
{
    /// <summary>Promissory Note number (loan_data.loan_no).</summary>
    public string PnNumber { get; set; } = string.Empty;

    /// <summary>Loan product code.</summary>
    public string? ProductCode { get; set; }

    /// <summary>Loan product description (from loan_product lookup).</summary>
    public string? ProductDescription { get; set; }

    /// <summary>Creation type code (0=New, 1=Reloan, 2=Reconstructed, 3=Continuation, 4=Extension, 5=Renewal, 6=Additional Loan).</summary>
    public byte? CreationType { get; set; }

    /// <summary>Creation type label.</summary>
    public string? CreationTypeLabel { get; set; }

    /// <summary>Principal amount.</summary>
    public decimal? Principal { get; set; }

    /// <summary>Applied principal (proposed amount).</summary>
    public decimal? AppliedPrincipal { get; set; }

    /// <summary>Current principal balance.</summary>
    public decimal? PrincipalBalance { get; set; }

    /// <summary>Amortization amount.</summary>
    public decimal? AmortizationAmount { get; set; }

    /// <summary>Outstanding balance (principal + interest).</summary>
    public decimal? OutstandingBalance { get; set; }

    /// <summary>Date granted.</summary>
    public DateTime? DateGranted { get; set; }

    /// <summary>Date maturity.</summary>
    public DateTime? DateMaturity { get; set; }

    /// <summary>Loan status code.</summary>
    public byte? StatusCode { get; set; }

    /// <summary>Loan status description (from loan_status lookup).</summary>
    public string? StatusDescription { get; set; }

    /// <summary>Close date (if paid off).</summary>
    public DateTime? CloseDate { get; set; }

    /// <summary>Granted interest rate.</summary>
    public decimal? GrantedRate { get; set; }

    /// <summary>Effective interest rate.</summary>
    public decimal? EffectiveRate { get; set; }

    /// <summary>Loan purpose.</summary>
    public string? Purpose { get; set; }

    /// <summary>Payment interval in months.</summary>
    public int? PaymentInterval { get; set; }

    /// <summary>Total amortization count (term).</summary>
    public int? TotalAmortization { get; set; }
}

// ============================================================================
// ACTIVE LOANS BY ACCOUNT — Mirrors the "Active Loans by existing borrower"
// reference query: webloan.dbo.loan_data where acct_no + bch='000' + is_loan=1
// + loan_status != 10, top 10 by date_granted desc.
// ============================================================================

/// <summary>
/// Response for GET /api/webloans/cis/{cisNo}/accounts/{accountNo}/active-loans.
/// Returns up to 10 active (non-status-10) PN rows for the given account,
/// ordered by date granted (most recent first).
/// </summary>
public class ActiveLoansResponse
{
    /// <summary>Account number the active loans belong to.</summary>
    public string AccountNo { get; set; } = string.Empty;

    /// <summary>CIS number that owns the account (echoed for caller convenience).</summary>
    public string CisNo { get; set; } = string.Empty;

    /// <summary>Up to 10 active PN records (loan_data rows) for this account.</summary>
    public List<ActiveLoanItem> Loans { get; set; } = new();
}

/// <summary>
/// One active (non-closed) loan row, shaped to match the reference "Active Loans
/// by existing borrower" query exactly. The frontend renders these directly.
/// </summary>
public class ActiveLoanItem
{
    /// <summary>Promissory Note number (loan_data.loan_no).</summary>
    public string LoanNo { get; set; } = string.Empty;

    /// <summary>Original principal amount (loan_data.principal).</summary>
    public decimal? Principal { get; set; }

    /// <summary>Current principal balance (loan_data.principal_bal).</summary>
    public decimal? PrincipalBalance { get; set; }

    /// <summary>Date the loan was granted (loan_data.date_granted, date only).</summary>
    public DateTime? DateGranted { get; set; }

    /// <summary>Maturity date (loan_data.date_maturity, date only).</summary>
    public DateTime? DateMaturity { get; set; }

    /// <summary>Loan product code (loan_data.loan_product, e.g. "PL", "MPL").</summary>
    public string? LoanProduct { get; set; }

    /// <summary>Loan product description resolved from dbo.loan_product.</summary>
    public string? LoanProductDescription { get; set; }

    /// <summary>Raw loan status code (loan_data.loan_status).</summary>
    public byte? StatusCode { get; set; }

    /// <summary>
    /// Loan status label resolved from dbo.loan_status. Falls back to the raw
    /// code as a string if the lookup table has no matching row.
    /// </summary>
    public string? StatusDescription { get; set; }

    /// <summary>
    /// Combined "product - status" string for display (e.g. "PL - Current").
    /// Matches the reference query's product_status column verbatim.
    /// </summary>
    public string? ProductStatus { get; set; }
}

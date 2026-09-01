using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Features.WebLoans;
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
public class BranchAndTypeSection
{
    public string? Type { get; set; }

    public int? TypeCode { get; set; }

    public string? BranchCode { get; set; }
    public string? RequestingOfficer { get; set; }

    public string? CisNo { get; set; }

    public List<string> Lai { get; set; } = new();
}

public class PersonalInformationSection
{
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Suffix { get; set; }
    public DateTime? Birthdate { get; set; }
    public string? Address { get; set; }

    public string? AgencyName { get; set; }
    public byte? AgencyTypeCode { get; set; }
    public string? AgencyType { get; set; }

    public string? PositionTitle { get; set; }
    public string? LengthOfService => null;

    public string? RegionCode { get; set; }
    public string? DivisionCode { get; set; }
    public string? StationCode { get; set; }
    public string? EmployeeNo { get; set; }
    public string? MisAgency { get; set; }
    public string? MisAgencyName { get; set; }
}

public class OptionalInformationSection
{
    public string? Referrer => null;
    public string? School => null;
}

public class LoanInformationSection
{
    public string? ProductCode { get; set; }
    public string? ProductDescription { get; set; }

    public int? TermMonths { get; set; }

    public int? PaymentIntervalMonths { get; set; }

    public decimal? InterestRate { get; set; }

    public string? Purpose { get; set; }

    public decimal? ProposedAmount { get; set; }

    public decimal? Nthp => null;

    public DateTime? NthpDate => null;
}
public class DeviationSection
{
    public bool HasDeviations => false;
    public List<string> Deviations { get; set; } = new();
}

public class OutstandingLoanItem
{
    public string Pn { get; set; } = string.Empty;
    public string? AccountNo { get; set; }
    public decimal? PrincipalBalance { get; set; }
    public decimal? Amortization { get; set; }
    public decimal? OutstandingBalance { get; set; }
    public DateTime? DateGranted { get; set; }
    public DateTime? DateMaturity { get; set; }
    public string? Status { get; set; }
}

public class EbiReloanAccountItem
{
    public string Pn { get; set; } = string.Empty;
    public string? Name { get; set; }

    public decimal? ExistingDeductions => null;

    public decimal? PayToClose => null;

    public decimal? PrincipalBalance { get; set; }
    public string? Status { get; set; }
}
public class BuyOutAccountItem
{
    public string Pn { get; set; } = string.Empty;
    public string? Name { get; set; }
    public decimal? Amortization { get; set; }
    public decimal? OutstandingBalance { get; set; }
}
public class IncomingLoanItem
{
    public string? Name { get; set; }
    public decimal? Deductions { get; set; }
    public string? Remarks { get; set; }
}

public class CisSearchResult
{
    public string CisNo { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string BranchCode { get; set; } = string.Empty;

    public List<CisAccountSummary> Accounts { get; set; } = new();
}
public class CisAccountSummary
{
    public string AccountNo { get; set; } = string.Empty;

    public string? AccountName { get; set; }

    public string? AccountAddress { get; set; }

    public string? MisGroup { get; set; }

    public int PnCount { get; set; }
}

public class AccountWithPnsResponse
{
    public string AccountNo { get; set; } = string.Empty;

    public string? AccountName { get; set; }

    public string? AccountAddress { get; set; }

    public string? MisGroup { get; set; }

    public List<PnRecord> PnRecords { get; set; } = new();
}
public class PnRecord
{
    public string PnNumber { get; set; } = string.Empty;
    public string? AccountNo { get; set; }

    public string? ProductCode { get; set; }

    public string? ProductDescription { get; set; }

    public byte? CreationType { get; set; }

    public string? CreationTypeLabel { get; set; }

    public decimal? Principal { get; set; }

    public decimal? AppliedPrincipal { get; set; }

    public decimal? PrincipalBalance { get; set; }

    public decimal? AmortizationAmount { get; set; }

    public decimal? OutstandingBalance { get; set; }

    public DateTime? DateGranted { get; set; }

    public DateTime? DateMaturity { get; set; }

    public byte? StatusCode { get; set; }

    public string? StatusDescription { get; set; }

    public DateTime? CloseDate { get; set; }

    public decimal? GrantedRate { get; set; }

    public decimal? EffectiveRate { get; set; }

    public string? Purpose { get; set; }

    public int? PaymentInterval { get; set; }

    public int? TotalAmortization { get; set; }
}

// ============================================================================
// ACTIVE LOANS BY ACCOUNT — Mirrors the "Active Loans by existing borrower"
// reference query: webloan.dbo.loan_data where acct_no + bch='000' + is_loan=1
// + loan_status != 10, top 10 by date_granted desc.
// ============================================================================
public class ActiveLoansResponse
{
    public string AccountNo { get; set; } = string.Empty;

    public string CisNo { get; set; } = string.Empty;

    public List<ActiveLoanItem> Loans { get; set; } = new();
}
public class ActiveLoanItem
{
    public string LoanNo { get; set; } = string.Empty;

    public decimal? Principal { get; set; }

    public decimal? PrincipalBalance { get; set; }

    public DateTime? DateGranted { get; set; }

    public DateTime? DateMaturity { get; set; }

    public string? LoanProduct { get; set; }

    public string? LoanProductDescription { get; set; }

    public byte? StatusCode { get; set; }
    public string? StatusDescription { get; set; }
    public string? ProductStatus { get; set; }
}

// ============================================================================
// PAGINATED WEBLOAN RESPONSE SHAPES
// ============================================================================
public class AccountWithPnsPagedItem
{
    public string AccountNo { get; set; } = string.Empty;

    public string? AccountName { get; set; }

    public string? AccountAddress { get; set; }

    public string? MisGroup { get; set; }
    public PagedResponse<PnRecord> PnPage { get; set; } =
        new(Array.Empty<PnRecord>(), TotalCount: 0, Page: 1, PageSize: PaginationRequest.DefaultPageSize);
}

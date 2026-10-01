namespace EBI.ALAS.Api.Features.WebLoans;
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
    public static string CreationTypeLabel(byte? code) => code switch
    {
        0 => "New Loan",
        1 => "Reloan",
        2 => "Restructured",
        6 => "Additional Loan",
        _ => "Unknown"
    };
}
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
    string? LengthOfService);
public record AccountDto(
    string BankCode,
    string BranchCode,
    string AccountNo,
    string AccountId,
    string? Name,
    decimal? CreditLimit,
    decimal? UsedCredit,
    string? BorrowerType);
public record OutstandingLoansResponse(
    string CisNo,
    string AccountId,
    string BranchCode,
    string AccountNo,
    IReadOnlyList<OutstandingLoanDto> Loans);
public record OutstandingLoanDto(
    string? LoanNo,
    decimal? Principal,
    decimal? PrincipalBalance,
    decimal? AmortAmount,
    DateTime? DateGranted,
    DateTime? DateMaturity,
    string ProductCode,
    string ProductStatus,
    string ProductWithDescription);
public record PendingLoanResponse(
    string CisNo,
    string AccountId,
    string BranchCode,
    string AccountNo,
    IReadOnlyList<PendingLoanDto> Loans,
    string? Nthp,
    DateTime? NthpDate);
public record PendingLoanDto(
    string LoanNo,
    decimal? Principal,
    decimal? GrantedRate,
    int? TotalTermDays,
    int? PolicyTermMonths,
    string ProductWithDescription,
    string? LoanPurpose,
    byte? CreationType,
    string CreationTypeLabel,
    decimal? CDocStamp);
public record LoanProductDto(
    string IdCode,
    string Description);
public record CatLoanClassResponse(
    string Bch,
    string LoanNo,
    string LoanProduct,
    string? CatLoanClass);
public record CocreeStatusResponse(
    string CisNo,
    bool IsComplete,
    IReadOnlyList<CocreeItemStatus> Items);
public record CocreeItemStatus(
    string ItemCode,
    DateTime? Submitted,
    string? Description,
    DateTime? Expiration);
public static class WebLoanAccountId
{
    public static string Format(string branchCode, string accountNo)
        => $"{branchCode}-{accountNo}";
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

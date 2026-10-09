namespace Ebi.Alas.Api.Features.WebLoans;

public sealed record CisSearchResult(string CifNo, string FullName, string? Address);

public sealed record CisBorrower(
    string CisNo,
    string FirstName,
    string? MiddleName,
    string LastName,
    string? Title,
    string? Appelation,
    string? BirthDate,
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

public sealed record CisAccount(
    string BankCode,
    string BranchCode,
    string AccountNo,
    string AccountId,
    string? Name,
    decimal? CreditLimit,
    decimal? UsedCredit,
    string? BorrowerType);

public sealed record CisDetail(CisBorrower Borrower, IReadOnlyList<CisAccount> Accounts);

/// <summary>Catalog row from webloan. Retired when expiration is set.</summary>
public sealed record LoanProductLookup(
    string ProductCode,
    string ProductName,
    bool IsRetired,
    decimal InterestRatePerMonth = 0m);

public sealed record OutstandingLoanRow(string AccountNo, string Status, decimal Balance);

public sealed record OutstandingLoanDto(
    string? LoanNo,
    decimal? Principal,
    decimal? PrincipalBalance,
    decimal? AmortAmount,
    string? DateGranted,
    string? DateMaturity,
    string ProductCode,
    string ProductStatus,
    string ProductWithDescription);

public sealed record OutstandingLoansResponse(
    string CisNo,
    string AccountId,
    string BranchCode,
    string AccountNo,
    IReadOnlyList<OutstandingLoanDto> Loans);

public sealed record PendingLoanDto(
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

public sealed record PendingLoanResponse(
    string CisNo,
    string AccountId,
    string BranchCode,
    string AccountNo,
    IReadOnlyList<PendingLoanDto> Loans,
    string? Nthp,
    string? NthpDate);

public static class WebLoanAccountId
{
    public static string Format(string branchCode, string accountNo) => $"{branchCode}-{accountNo}";

    public static (string BranchCode, string AccountNo) Parse(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        var idx = accountId.IndexOf('-');
        if (idx <= 0 || idx == accountId.Length - 1)
        {
            throw new ArgumentException(
                $"accountId '{accountId}' must be '<branchCode>-<accountNo>'.",
                nameof(accountId));
        }

        return (accountId[..idx].Trim(), accountId[(idx + 1)..].Trim());
    }
}

public interface IWebLoanReader
{
    Task<IReadOnlyList<CisSearchResult>> SearchCisAsync(string keyword, int max, CancellationToken cancellationToken);

    Task<CisDetail?> GetCisDetailAsync(string cisNo, CancellationToken cancellationToken);

    Task<IReadOnlyList<LoanProductLookup>> GetActiveProductsAsync(int max, CancellationToken cancellationToken);

    Task<IReadOnlyList<LoanProductLookup>> GetAllProductsAsync(int max, CancellationToken cancellationToken);

    Task<IReadOnlyList<OutstandingLoanRow>> GetOutstandingAsync(string cifNo, int max, CancellationToken cancellationToken);

    Task<OutstandingLoansResponse?> GetAccountOutstandingLoansAsync(
        string cisNo, string accountId, int pageSize, int pageNumber, CancellationToken cancellationToken);

    Task<PendingLoanResponse?> GetAccountPendingLoanAsync(
        string cisNo, string accountId, CancellationToken cancellationToken);
}

public sealed class NullWebLoanReader : IWebLoanReader
{
    public Task<IReadOnlyList<CisSearchResult>> SearchCisAsync(string keyword, int max, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CisSearchResult>>([]);

    public Task<CisDetail?> GetCisDetailAsync(string cisNo, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cisNo);
        return Task.FromResult<CisDetail?>(null);
    }

    public Task<IReadOnlyList<LoanProductLookup>> GetActiveProductsAsync(int max, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LoanProductLookup>>([]);

    public Task<IReadOnlyList<LoanProductLookup>> GetAllProductsAsync(int max, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LoanProductLookup>>([]);

    public Task<IReadOnlyList<OutstandingLoanRow>> GetOutstandingAsync(string cifNo, int max, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<OutstandingLoanRow>>([]);

    public Task<OutstandingLoansResponse?> GetAccountOutstandingLoansAsync(
        string cisNo, string accountId, int pageSize, int pageNumber, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cisNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return Task.FromResult<OutstandingLoansResponse?>(null);
    }

    public Task<PendingLoanResponse?> GetAccountPendingLoanAsync(
        string cisNo, string accountId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cisNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return Task.FromResult<PendingLoanResponse?>(null);
    }
}

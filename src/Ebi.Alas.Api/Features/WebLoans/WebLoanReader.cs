namespace Ebi.Alas.Api.Features.WebLoans;

public sealed record CisSearchResult(string CifNo, string FullName, string? Address);

public sealed record LoanProductLookup(string ProductCode, string ProductName, decimal InterestRatePerMonth);

public sealed record OutstandingLoanRow(string AccountNo, string Status, decimal Balance);

public interface IWebLoanReader
{
    Task<IReadOnlyList<CisSearchResult>> SearchCisAsync(string keyword, int max, CancellationToken cancellationToken);

    Task<IReadOnlyList<LoanProductLookup>> GetActiveProductsAsync(int max, CancellationToken cancellationToken);

    Task<IReadOnlyList<OutstandingLoanRow>> GetOutstandingAsync(string cifNo, int max, CancellationToken cancellationToken);
}

public sealed class NullWebLoanReader : IWebLoanReader
{
    public Task<IReadOnlyList<CisSearchResult>> SearchCisAsync(string keyword, int max, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CisSearchResult>>([]);

    public Task<IReadOnlyList<LoanProductLookup>> GetActiveProductsAsync(int max, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LoanProductLookup>>([]);

    public Task<IReadOnlyList<OutstandingLoanRow>> GetOutstandingAsync(string cifNo, int max, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<OutstandingLoanRow>>([]);
}

namespace EBI.ALAS.Api.Features.WebLoans;

/// <summary>
/// Read-only access to borrower/loan data in the WebLoan database.
/// </summary>
public interface IWebLoanService
{
    /// <summary>
    /// Fetches the full borrower profile (personal info, loan info, outstanding loans,
    /// reloan accounts) from the webloan DB by CIS number. Returns null if not found.
    /// </summary>
    Task<WebLoanBorrowerResponse?> GetBorrowerByCisAsync(string cisNo, CancellationToken ct = default);

    /// <summary>
    /// Step 1: Search CIS — returns basic borrower info + list of accounts for selection.
    /// Lightweight query for the initial search results screen.
    /// </summary>
    Task<CisSearchResult?> SearchCisAsync(string cisNo, CancellationToken ct = default);

    /// <summary>
    /// Step 2: Get account detail with all PN records — returns full PN list for selected account.
    /// </summary>
    Task<AccountWithPnsResponse?> GetAccountWithPnsAsync(string cisNo, string accountNo, CancellationToken ct = default);
}

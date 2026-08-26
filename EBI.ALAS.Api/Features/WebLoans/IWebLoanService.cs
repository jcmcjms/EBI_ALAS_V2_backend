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
}

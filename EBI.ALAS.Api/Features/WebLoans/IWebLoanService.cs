using EBI.ALAS.Api.Common.Models;

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
    /// Paginated variant of <see cref="GetBorrowerByCisAsync"/>. Bounds the
    /// JSON payload size for corporate borrowers with many accounts by
    /// returning a paged list of accounts where each account carries a
    /// bounded PN slice (top <see cref="Constants.RecentPnPerAccount"/> recent
    /// PNs). Returns null when the CIS does not exist.
    /// </summary>
    /// <remarks>
    /// Added as part of the 500+ user hardening sprint. Kept as a separate
    /// method (not an overload) so the original full-profile response shape
    /// is preserved for callers that have not opted into pagination.
    /// </remarks>
    Task<PagedResponse<AccountWithPnsPagedItem>?> GetBorrowerByCisPagedAsync(
        string cisNo,
        PaginationRequest pagination,
        CancellationToken ct = default);

    /// <summary>
    /// Step 1: Search CIS — returns basic borrower info + list of accounts for selection.
    /// Lightweight query for the initial search results screen.
    /// </summary>
    Task<CisSearchResult?> SearchCisAsync(string cisNo, CancellationToken ct = default);

    /// <summary>
    /// Step 2: Get account detail with all PN records — returns full PN list for selected account.
    /// </summary>
    Task<AccountWithPnsResponse?> GetAccountWithPnsAsync(string cisNo, string accountNo, CancellationToken ct = default);

    /// <summary>
    /// Paginated variant of <see cref="GetAccountWithPnsAsync"/>. Returns a single
    /// page of PN records for the (CIS, account) pair plus the total count for
    /// paging. Returns null when the account does not belong to the given CIS
    /// (preserving the existing IDOR protection on this route).
    /// </summary>
    Task<PagedResponse<PnRecord>?> GetAccountPromissoryNotesPagedAsync(
        string cisNo,
        string accountNo,
        PaginationRequest pagination,
        CancellationToken ct = default);

    /// <summary>
    /// Get up to 10 active loans for a (cis, account) pair — mirrors the reference
    /// "Active Loans by existing borrower" SQL exactly:
    ///   <c>SELECT TOP 10 ... FROM dbo.loan_data
    ///    WHERE acct_no = @acct AND bch = '000'
    ///      AND webloan.dbo.is_loan(loan_no) = 1
    ///      AND loan_status != 10
    ///    ORDER BY date_granted DESC</c>.
    /// Returns null if the account does not belong to the given CIS.
    /// </summary>
    Task<ActiveLoansResponse?> GetActiveLoansByAccountAsync(string cisNo, string accountNo, CancellationToken ct = default);
}

/// <summary>
/// Internal constants used by <see cref="WebLoanService"/> when assembling
/// borrower profiles. Kept in a separate type so call sites document the
/// business rule next to the number rather than as inline magic numbers.
/// </summary>
internal static class Constants
{
    /// <summary>
    /// Default number of recent PN records returned per account when the caller
    /// does not paginate the borrower profile. Matches the original
    /// pre-pagination behaviour so existing clients see no payload growth change.
    /// </summary>
    public const int RecentPnPerAccount = 5;
}

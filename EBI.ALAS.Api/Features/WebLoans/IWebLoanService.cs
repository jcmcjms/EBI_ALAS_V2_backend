using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Features.WebLoans;
public interface IWebLoanService
{
    Task<WebLoanBorrowerResponse?> GetBorrowerByCisAsync(string cisNo, CancellationToken ct = default);
    Task<PagedResponse<AccountWithPnsPagedItem>?> GetBorrowerByCisPagedAsync(
        string cisNo,
        PaginationRequest pagination,
        CancellationToken ct = default);

    Task<CisSearchResult?> SearchCisAsync(string cisNo, CancellationToken ct = default);
    Task<AccountWithPnsResponse?> GetAccountWithPnsAsync(
        string cisNo,
        string accountNo,
        int limit = 500,
        CancellationToken ct = default);
    Task<PagedResponse<PnRecord>?> GetAccountPromissoryNotesPagedAsync(
        string cisNo,
        string accountNo,
        PaginationRequest pagination,
        CancellationToken ct = default);
    Task<ActiveLoansResponse?> GetActiveLoansByAccountAsync(string cisNo, string accountNo, CancellationToken ct = default);
}
internal static class Constants
{
    public const int RecentPnPerAccount = 5;
}

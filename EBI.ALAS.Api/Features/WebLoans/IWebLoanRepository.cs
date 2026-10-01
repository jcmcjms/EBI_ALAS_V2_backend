using EBI.ALAS.Api.Features.WebLoans;
namespace EBI.ALAS.Api.Features.WebLoans;
public interface IWebLoanRepository
{
    Task<CisInfo?> GetCisInfoAsync(string cisNo, CancellationToken ct = default);
    Task<CisInfoMiscData?> GetAgencyTypeAsync(string cisNo, CancellationToken ct = default);
    Task<CheckListData?> GetLengthOfServiceAsync(string cisNo, CancellationToken ct = default);
    Task<MisGroup?> GetMisGroupByIdCodeAsync(string idCode, CancellationToken ct = default);
    Task<IReadOnlyList<MisGroup>> GetMisGroupsByPathsAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct = default);
    Task<IReadOnlyList<MisGroup>> GetSolicitorsByPathsAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct = default);
    Task<IReadOnlyList<LoanAcctInfo>> GetAccountsByCisAsync(
        string cisNo,
        string? branchCode = null,
        CancellationToken ct = default);
    Task<bool> AccountBelongsToCisAsync(
        string cisNo,
        string branchCode,
        string accountNo,
        CancellationToken ct = default);
    Task<IReadOnlyList<OutstandingLoanRow>> GetOutstandingLoansAsync(
        string branchCode,
        string accountNo,
        int pageSize = 50,
        int pageNumber = 1,
        CancellationToken ct = default);
    Task<IReadOnlyList<PendingLoanRow>> GetPendingLoansAsync(
        string branchCode,
        string accountNo,
        CancellationToken ct = default);
    Task<IReadOnlyList<LoanProductLookup>> GetActiveLoanProductsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LoanProductLookup>> GetAllLoanProductsAsync(CancellationToken ct = default);
    Task<string?> GetCatLoanClassAsync(
        string branchCode,
        string loanNo,
        string productCode,
        CancellationToken ct = default);
    Task<IReadOnlyList<CheckListData>> GetCocreeItemsAsync(
        string cisNo,
        CancellationToken ct = default);
    Task<PreLoanData?> GetPreLoanDataByLoanNoAsync(
        string loanNo,
        CancellationToken ct = default);
}

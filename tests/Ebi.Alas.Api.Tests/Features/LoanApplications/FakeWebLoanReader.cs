using Ebi.Alas.Api.Features.WebLoans;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

/// <summary>WebLoan stub with a complete COCRE set and usable client profile.</summary>
internal sealed class FakeWebLoanReader : IWebLoanReader
{
    public HashSet<string> PresentCocree { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "CCR01", "CCR02", "CCR03", "CCR04", "CCR05",
        "CCR06", "CCR07", "CCR08", "CCR09", "CCR10", "CCR11",
    };

    public string? Address { get; set; } = "123 Rizal St, Manila";

    public string? Phone { get; set; } = "09171234567";

    public bool CisExists { get; set; } = true;

    public bool HasFirstAmortization { get; set; }

    public Task<IReadOnlyList<CisSearchResult>> SearchCisAsync(string keyword, int max, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CisSearchResult>>([]);

    public Task<IReadOnlyList<WebLoanChecklistRow>> GetChecklistItemsAsync(string cisNo, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<WebLoanChecklistRow>>(
            PresentCocree.Select(c => new WebLoanChecklistRow(c, c, DateTime.UtcNow)).ToList());

    public Task<IReadOnlyList<string>> GetMissingCocreeItemsAsync(
        string cisNo,
        IReadOnlyCollection<string> requiredItems,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>(
            requiredItems.Where(c => !PresentCocree.Contains(c)).ToList());

    public Task<bool> HasFirstAmortizationAsync(string loanNo, CancellationToken cancellationToken)
        => Task.FromResult(HasFirstAmortization);

    public Task<IReadOnlyList<WebLoanChecklistCatalogRow>> GetChecklistCatalogAsync(
        IReadOnlyCollection<string> idCodes,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<WebLoanChecklistCatalogRow>>([]);

    public Task<CisDetail?> GetCisDetailAsync(string cisNo, CancellationToken cancellationToken)
    {
        if (!CisExists)
        {
            return Task.FromResult<CisDetail?>(null);
        }

        return Task.FromResult<CisDetail?>(new CisDetail(
            new CisBorrower(
                cisNo, "Test", null, "Client", null, null, null, Address,
                null, null, null, null, null, null, null, null, null, null, Phone),
            []));
    }

    public Task<IReadOnlyList<LoanProductLookup>> GetActiveProductsAsync(int max, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LoanProductLookup>>([]);

    public Task<IReadOnlyList<LoanProductLookup>> GetAllProductsAsync(int max, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LoanProductLookup>>([]);

    public Task<IReadOnlyList<OutstandingLoanRow>> GetOutstandingAsync(string cifNo, int max, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<OutstandingLoanRow>>([]);

    public Task<OutstandingLoansResponse?> GetAccountOutstandingLoansAsync(
        string cisNo, string accountId, int pageSize, int pageNumber, CancellationToken cancellationToken)
        => Task.FromResult<OutstandingLoansResponse?>(null);

    public Task<PendingLoanResponse?> GetAccountPendingLoanAsync(
        string cisNo, string accountId, CancellationToken cancellationToken)
        => Task.FromResult<PendingLoanResponse?>(null);

    public Task<string?> GetCatLoanClassAsync(string branchCode, string loanNo, string productCode, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

    public Task<WebLoanLoanDataRow?> GetLoanByNoAsync(string loanNo, CancellationToken cancellationToken)
        => Task.FromResult<WebLoanLoanDataRow?>(null);
}

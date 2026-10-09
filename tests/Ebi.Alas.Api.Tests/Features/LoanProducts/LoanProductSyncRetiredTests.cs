using Ebi.Alas.Api.Features.LoanProducts;
using Ebi.Alas.Api.Features.WebLoans;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanProducts;

public sealed class LoanProductSyncRetiredTests
{
    private sealed class StubWebLoanReader(IReadOnlyList<LoanProductLookup> products) : IWebLoanReader
    {
        public Task<IReadOnlyList<CisSearchResult>> SearchCisAsync(string keyword, int max, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<CisSearchResult>>([]);

        public Task<CisDetail?> GetCisDetailAsync(string cisNo, CancellationToken cancellationToken)
            => Task.FromResult<CisDetail?>(null);

        public Task<IReadOnlyList<LoanProductLookup>> GetActiveProductsAsync(int max, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LoanProductLookup>>(products.Where(p => !p.IsRetired).ToList());

        public Task<IReadOnlyList<LoanProductLookup>> GetAllProductsAsync(int max, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LoanProductLookup>>(products);

        public Task<IReadOnlyList<OutstandingLoanRow>> GetOutstandingAsync(string cifNo, int max, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<OutstandingLoanRow>>([]);

        public Task<OutstandingLoansResponse?> GetAccountOutstandingLoansAsync(
            string cisNo, string accountId, int pageSize, int pageNumber, CancellationToken cancellationToken)
            => Task.FromResult<OutstandingLoansResponse?>(null);

        public Task<PendingLoanResponse?> GetAccountPendingLoanAsync(
            string cisNo, string accountId, CancellationToken cancellationToken)
            => Task.FromResult<PendingLoanResponse?>(null);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"loan-product-sync-retired-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public async Task HandleAsync_MarksProductsRetiredFromWebloanExpiration()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var reader = new StubWebLoanReader(
        [
            new LoanProductLookup("A16", "Salary Loan", IsRetired: false),
            new LoanProductLookup("OLD", "Legacy Loan", IsRetired: true),
        ]);
        var handler = new SyncLoanProductsHandler(db, reader, new FixedTimeProvider(now));

        await handler.HandleAsync(CancellationToken.None);

        Assert.True((await db.LoanProducts.SingleAsync(p => p.Code == "A16")).IsActive);
        Assert.True((await db.LoanProducts.SingleAsync(p => p.Code == "OLD")).ToResponse().IsRetired);
        Assert.False((await db.LoanProducts.SingleAsync(p => p.Code == "OLD")).IsActive);
    }

    [Fact]
    public async Task HandleAsync_UpdatesRetiredStateFromWebloanOnExistingRows()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var existing = LoanProduct.Create("A16", "Salary Loan", 0.02m, 30, 365, now);
        existing.Deactivate();
        db.LoanProducts.Add(existing);
        await db.SaveChangesAsync();

        var reader = new StubWebLoanReader(
        [
            new LoanProductLookup("A16", "Salary Loan", IsRetired: false),
        ]);
        var handler = new SyncLoanProductsHandler(db, reader, new FixedTimeProvider(now));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(1, result.Updated);
        Assert.True((await db.LoanProducts.SingleAsync(p => p.Code == "A16")).IsActive);
    }
}

using Ebi.Alas.Api.Features.LoanProducts;
using Ebi.Alas.Api.Features.WebLoans;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanProducts;

public sealed class LoanProductSyncHandlerTests
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
            .UseInMemoryDatabase($"loan-product-sync-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public async Task HandleAsync_AddsNewCatalogProducts()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var reader = new StubWebLoanReader(
        [
            new LoanProductLookup("A16", "Salary Loan", IsRetired: false, InterestRatePerMonth: 0.02m),
            new LoanProductLookup("C02", "Business Loan", IsRetired: false, InterestRatePerMonth: 0.025m),
        ]);
        var handler = new SyncLoanProductsHandler(db, reader, new FixedTimeProvider(now));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(2, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Preserved);
        Assert.Equal(now, result.SyncedAt);
        Assert.Equal(2, await db.LoanProducts.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_UpdatesCatalogFieldsAndPreservesPolicy()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var existing = LoanProduct.Create("A16", "Old Name", 0.01m, 30, 365, now);
        existing.UpdatePolicy(1000m, 5000m, 30, 365, 10m, 20m, 30m, 0.10m);
        db.LoanProducts.Add(existing);
        await db.SaveChangesAsync();

        var reader = new StubWebLoanReader(
        [
            new LoanProductLookup("A16", "New Name", IsRetired: false, InterestRatePerMonth: 0.03m),
        ]);
        var handler = new SyncLoanProductsHandler(db, reader, new FixedTimeProvider(now));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Preserved);

        var reloaded = await db.LoanProducts.AsNoTracking().SingleAsync(p => p.Code == "A16");
        Assert.Equal("New Name", reloaded.Name);
        // Webloan catalog does not own the interest rate; policy rate is preserved.
        Assert.Equal(0.01m, reloaded.InterestRatePerMonth);
        Assert.Equal(1000m, reloaded.MinAmount);
        Assert.Equal(5000m, reloaded.MaxAmount);
        Assert.Equal(0.10m, reloaded.AdvanceInterestRate);
    }

    [Fact]
    public async Task HandleAsync_CountsUnchangedProductsAsPreserved()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var existing = LoanProduct.Create("A16", "Salary Loan", 0.02m, 30, 365, now);
        db.LoanProducts.Add(existing);
        await db.SaveChangesAsync();

        var reader = new StubWebLoanReader(
        [
            new LoanProductLookup("A16", "Salary Loan", IsRetired: false, InterestRatePerMonth: 0.02m),
        ]);
        var handler = new SyncLoanProductsHandler(db, reader, new FixedTimeProvider(now));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(1, result.Preserved);
    }
}

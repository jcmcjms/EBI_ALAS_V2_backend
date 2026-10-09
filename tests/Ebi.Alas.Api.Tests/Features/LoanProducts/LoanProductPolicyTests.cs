using Ebi.Alas.Api.Features.LoanProducts;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanProducts;

public sealed class LoanProductPolicyTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"loan-product-policy-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public void UpdatePolicy_PersistsEligibilityBoundsAndFees()
    {
        var product = LoanProduct.Create("A16", "Salary Loan", 0.02m, 30, 365, DateTimeOffset.UtcNow);

        product.UpdatePolicy(
            minAmount: 10_000m,
            maxAmount: 250_000m,
            minTermDays: 30,
            maxTermDays: 365,
            notarialFee: 500m,
            docStampFee: 200m,
            insuranceFee: 350m,
            advanceInterestRate: 0.12m);

        var response = product.ToResponse();
        Assert.Equal(10_000m, response.MinAmount);
        Assert.Equal(250_000m, response.MaxAmount);
        Assert.Equal(30, response.MinTermDays);
        Assert.Equal(365, response.MaxTermDays);
        Assert.Equal(500m, response.NotarialFee);
        Assert.Equal(200m, response.DocStampFee);
        Assert.Equal(350m, response.InsuranceFee);
        Assert.Equal(0.12m, response.AdvanceInterestRate);
        Assert.Equal("A16", response.Code);
        Assert.Equal("Salary Loan", response.Description);
        Assert.False(response.IsRetired);
    }

    [Fact]
    public void UpdatePolicy_RejectsMaxAmountBelowMinAmount()
    {
        var product = LoanProduct.Create("A16", "Salary Loan", 0.02m, 30, 365, DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(() => product.UpdatePolicy(
            minAmount: 100m,
            maxAmount: 50m,
            minTermDays: 30,
            maxTermDays: 365,
            notarialFee: 0m,
            docStampFee: 0m,
            insuranceFee: 0m,
            advanceInterestRate: 0.10m));
    }

    [Fact]
    public void UpdatePolicy_RejectsNegativeFees()
    {
        var product = LoanProduct.Create("A16", "Salary Loan", 0.02m, 30, 365, DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(() => product.UpdatePolicy(
            minAmount: 0m,
            maxAmount: 100m,
            minTermDays: 1,
            maxTermDays: 30,
            notarialFee: -1m,
            docStampFee: 0m,
            insuranceFee: 0m,
            advanceInterestRate: 0m));
    }

    [Fact]
    public void ToResponse_MapsCatalogNameToDescriptionAndActiveToIsRetired()
    {
        var product = LoanProduct.Create("C02", "Business Loan", 0.025m, 60, 720, DateTimeOffset.UtcNow);
        product.Deactivate();

        var response = product.ToResponse();

        Assert.Equal("Business Loan", response.Description);
        Assert.True(response.IsRetired);
    }

    [Fact]
    public async Task UpdatePolicyHandler_PersistsPolicyFields()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var product = LoanProduct.Create("A16", "Salary Loan", 0.02m, 30, 365, now);
        db.LoanProducts.Add(product);
        await db.SaveChangesAsync();

        var handler = new UpdateLoanProductPolicyHandler(db);
        var result = await handler.HandleAsync(
            "A16",
            new UpdateLoanProductPolicyRequest(
                MinAmount: 5_000m,
                MaxAmount: 100_000m,
                MinTermDays: 15,
                MaxTermDays: 180,
                NotarialFee: 250m,
                DocStampFee: 100m,
                InsuranceFee: 50m,
                AdvanceInterestRate: 0.10m),
            CancellationToken.None);

        Assert.Equal(5_000m, result.MinAmount);
        Assert.Equal(100_000m, result.MaxAmount);
        Assert.Equal(0.10m, result.AdvanceInterestRate);

        var reloaded = await db.LoanProducts.AsNoTracking().SingleAsync(p => p.Code == "A16");
        Assert.Equal(5_000m, reloaded.MinAmount);
        Assert.Equal(100_000m, reloaded.MaxAmount);
        Assert.Equal(250m, reloaded.NotarialFee);
    }
}

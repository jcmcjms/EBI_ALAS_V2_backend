using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Persistence;

public sealed class WebLoanDbContextReadOnlyTests
{
    private static WebLoanDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<WebLoanDbContext>()
            .UseInMemoryDatabase($"webloan-ro-{Guid.NewGuid():N}")
            .Options;
        return new WebLoanDbContext(options);
    }

    [Fact]
    public void SaveChanges_Throws()
    {
        using var db = CreateDb();
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task SaveChangesAsync_Throws()
    {
        await using var db = CreateDb();
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
}

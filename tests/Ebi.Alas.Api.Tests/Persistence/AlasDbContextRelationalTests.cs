using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using LoanApplication = Ebi.Alas.Api.Features.LoanApplications.Domain.LoanApplication;
using LoanType = Ebi.Alas.Api.Features.LoanApplications.Domain.LoanType;
using User = Ebi.Alas.Api.Features.Users.Domain.User;
using UserRole = Ebi.Alas.Api.Features.Users.Domain.UserRole;

namespace Ebi.Alas.Api.Tests.Persistence;

public sealed class AlasDbContextRelationalTests
{
    private static AlasDbContext CreateSqlite()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), $"alas-test-{Guid.NewGuid():N}.db")}")
            .Options;
        var db = new AlasDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task User_UniqueUserName_Enforced()
    {
        await using var db = CreateSqlite();
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(User.Create("ada", "hash", "Ada", "a@x.com", "011", UserRole.Encoder, now));
        await db.SaveChangesAsync();

        db.Users.Add(User.Create("ada", "hash2", "Ada2", "b@x.com", "011", UserRole.Encoder, now));
        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Loan_LamId_IsUnique()
    {
        await using var db = CreateSqlite();
        var now = DateTimeOffset.UtcNow;
        var loan = LoanApplication.Create(
            "LAM-1", "G1", "Ada", "011", LoanType.New,
            100m, 30, 1m, 1m, 0, 100m, Guid.NewGuid(), now);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var dup = LoanApplication.Create(
            "LAM-1", "G2", "Bob", "011", LoanType.New,
            100m, 30, 1m, 1m, 0, 100m, Guid.NewGuid(), now);
        db.LoanApplications.Add(dup);
        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
    }
}

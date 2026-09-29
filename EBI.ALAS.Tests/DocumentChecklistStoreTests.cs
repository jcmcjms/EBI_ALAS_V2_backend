using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EBI.ALAS.Tests;

public class DocumentChecklistStoreTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly DocumentChecklistStore _store;

    public DocumentChecklistStoreTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);
        var time = Substitute.For<ITimeProvider>();
        time.UtcNow.Returns(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc));
        _store = new DocumentChecklistStore(_context, time);
    }

    public void Dispose() => _context.Dispose();

    private async Task<int> SeedLoanWithChecklistAsync(params (string Code, string Status)[] items)
    {
        var loan = new LoanApplication
        {
            LamId = "LAM-20260928-000002",
            ApplicationGroupNo = "APP-TEST-1",
            BranchCode = "011",
            FirstName = "Test",
            LastName = "Borrower",
            LoanNo = "LN-1",
            ProductCode = "P1",
            Product = "Product",
            Status = "ForChecking",
            DocumentsFlaggedAt = new DateTime(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc),
            DocumentFlagReason = "Missing requirements",
            CreatedById = 1,
        };
        _context.LoanApplications.Add(loan);
        await _context.SaveChangesAsync();

        foreach (var (code, status) in items)
        {
            _context.DocumentChecklists.Add(new DocumentChecklist
            {
                LoanApplicationId = loan.Id,
                Code = code,
                Name = code,
                Status = status,
            });
        }
        await _context.SaveChangesAsync();
        return loan.Id;
    }

    [Fact]
    public async Task CountUnresolvedAsync_counts_missing_and_pending()
    {
        // Mirrors LAM-20260928-000002: A2020, A2021, A2035, PIC02.
        var id = await SeedLoanWithChecklistAsync(
            ("A2020", "Missing"),
            ("A2021", "Missing"),
            ("A2035", "Missing"),
            ("PIC02", "Missing"),
            ("A2100", "Submitted"),
            ("A2101", "Verified"));

        var count = await _store.CountUnresolvedAsync(id, CancellationToken.None);

        Assert.Equal(4, count);
    }

    [Fact]
    public async Task CountUnresolvedAsync_counts_pending_as_unresolved()
    {
        var id = await SeedLoanWithChecklistAsync(
            ("A2020", "Pending"),
            ("A2021", "Verified"));

        var count = await _store.CountUnresolvedAsync(id, CancellationToken.None);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetUnresolvedAsync_matches_CountUnresolvedAsync()
    {
        var id = await SeedLoanWithChecklistAsync(
            ("A2020", "Missing"),
            ("A2021", "Pending"),
            ("A2035", "Submitted"),
            ("PIC02", "Verified"));

        var rows = await _store.GetUnresolvedAsync(id, CancellationToken.None);
        var count = await _store.CountUnresolvedAsync(id, CancellationToken.None);

        Assert.Equal(count, rows.Count);
        Assert.Equal(2, count);
    }

    [Fact]
    public void UnresolvedStatuses_is_missing_and_pending()
    {
        Assert.Equal(new[] { "Missing", "Pending" }, DocumentChecklistStore.UnresolvedStatuses);
    }
}

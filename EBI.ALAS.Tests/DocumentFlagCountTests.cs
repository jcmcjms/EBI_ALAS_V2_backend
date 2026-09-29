using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EBI.ALAS.Tests;

/// <summary>
/// Regression: detail badge showed "0 doc(s) flagged" while the list badge
/// showed 4. Root cause — GetLoanByIdWithRelatedCompiled does not Include
/// DocumentChecklists, so loan.DocumentChecklists.Count(...) on an
/// AsNoTracking entity silently returns 0.
/// </summary>
public class DocumentFlagCountTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly DocumentChecklistStore _store;
    private readonly LoanRepository _loans;

    public DocumentFlagCountTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);
        var time = new Mock<ITimeProvider>();
        time.Setup(t => t.UtcNow).Returns(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc));
        _store = new DocumentChecklistStore(_context, time.Object);
        _loans = new LoanRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    private async Task<int> SeedFlaggedLoanAsync(params string[] missingCodes)
    {
        var creator = new User
        {
            Id = 1,
            Username = "creator",
            FirstName = "Ana",
            LastName = "Cruz",
            Role = "Encoder",
            IsActive = true,
        };
        _context.Users.Add(creator);

        var loan = new LoanApplication
        {
            LamId = "LAM-20260928-000002",
            ApplicationGroupNo = "APP-20260928-000001",
            BranchCode = "011",
            FirstName = "Test",
            LastName = "Borrower",
            LoanNo = "LN-1",
            ProductCode = "P1",
            Product = "Product",
            Status = "ForChecking",
            DocumentsFlaggedAt = new DateTime(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc),
            DocumentFlagReason = "Incomplete documentary requirements",
            CreatedById = 1,
        };
        _context.LoanApplications.Add(loan);
        await _context.SaveChangesAsync();

        foreach (var code in missingCodes)
        {
            _context.DocumentChecklists.Add(new DocumentChecklist
            {
                LoanApplicationId = loan.Id,
                Code = code,
                Name = code,
                Status = "Missing",
            });
        }
        await _context.SaveChangesAsync();
        return loan.Id;
    }

    [Fact]
    public async Task Compiled_detail_query_does_not_load_DocumentChecklists()
    {
        var id = await SeedFlaggedLoanAsync("A2020", "A2021", "A2035", "PIC02");
        var loan = await _loans.GetByIdAsync(id, includeRelated: true);
        Assert.NotNull(loan);
        Assert.Empty(loan!.DocumentChecklists);
    }

    [Fact]
    public async Task CountUnresolvedAsync_returns_four_for_flagged_loan()
    {
        var id = await SeedFlaggedLoanAsync("A2020", "A2021", "A2035", "PIC02");
        var count = await _store.CountUnresolvedAsync(id, CancellationToken.None);
        Assert.Equal(4, count);
    }

    [Fact]
    public async Task List_and_detail_agree_on_unresolved_count()
    {
        var id = await SeedFlaggedLoanAsync("A2020", "A2021", "A2035", "PIC02");
        // List projection GetLoans currently hardcodes this pair; after GetLoans
        // switches to UnresolvedStatuses both sides share one source.
        var listStyle = await _context.DocumentChecklists
            .CountAsync(d => d.LoanApplicationId == id
                             && (d.Status == "Missing" || d.Status == "Pending"));
        var detailStyle = await _store.CountUnresolvedAsync(id, CancellationToken.None);
        Assert.Equal(listStyle, detailStyle);
        Assert.Equal(4, detailStyle);
    }

    [Fact]
    public async Task InMemory_navigation_count_is_zero_while_SQL_count_is_four()
    {
        // Pins the silent-zero trap: GetLoanById used loan.DocumentChecklists.Count(...)
        // on an unloaded AsNoTracking navigation. That path yields 0. The SQL COUNT
        // used after the fix yields 4. If this test fails, either the navigation is
        // being loaded (perf regression) or CountUnresolvedAsync broke.
        var id = await SeedFlaggedLoanAsync("A2020", "A2021", "A2035", "PIC02");

        var loan = await _loans.GetByIdAsync(id, includeRelated: true);
        Assert.NotNull(loan);

        var inMemory = loan!.DocumentChecklists.Count(d => d.Status == "Missing" || d.Status == "Pending");
        var sqlCount = await _store.CountUnresolvedAsync(id, CancellationToken.None);

        Assert.Equal(0, inMemory);
        Assert.Equal(4, sqlCount);
    }
}

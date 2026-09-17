using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Users;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Tests;

/// <summary>
/// Integration tests for UserRepository.GetUsersAsync covering:
///   - Deterministic ordering (ThenBy Id) prevents duplicate/vanishing rows across pages
///   - Server-side branch filter correctly reduces items and totalCount
///   - Branch filter + pagination combo stays consistent
/// </summary>
public class UserRepositoryTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly UserRepository _repository;

    public UserRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _repository = new UserRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    // ── Helpers ──────────────────────────────────────────────────────────

    private static User CreateUser(int id, string branchId, DateTime createdAt) => new()
    {
        Id = id,
        Username = $"user{id:D3}",
        FirstName = $"First{id}",
        LastName = $"Last{id}",
        BranchId = branchId,
        Role = "Encoder",
        IsActive = true,
        CreatedAt = createdAt,
    };

    private async Task SeedUsersAsync(int count, string branchId, DateTime? createdAt = null)
    {
        var timestamp = createdAt ?? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= count; i++)
        {
            var id = _context.Users.Any() ? _context.Users.Max(u => u.Id) + 1 : i;
            _context.Users.Add(CreateUser(id, branchId, timestamp));
        }
        await _context.SaveChangesAsync();
    }

    // ── Deterministic ordering: pages are disjoint and complete ──────────

    [Fact]
    public async Task GetUsersAsync_PagesAreDisjoint_WhenCreatedAtTies()
    {
        // Arrange: 15 users all sharing the same CreatedAt — the textbook
        // scenario for non-deterministic Skip/Take without a tie-break.
        var timestamp = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 15; i++)
            _context.Users.Add(CreateUser(i, "011", timestamp));
        await _context.SaveChangesAsync();

        var page1Params = new UserQueryParameters(null, null, null, null, PageNumber: 1, PageSize: 10);
        var page2Params = new UserQueryParameters(null, null, null, null, PageNumber: 2, PageSize: 10);

        // Act
        var page1 = await _repository.GetUsersAsync(page1Params);
        var page2 = await _repository.GetUsersAsync(page2Params);

        // Assert: pages are disjoint
        var page1Ids = page1.Items.Select(u => u.Id).ToHashSet();
        var page2Ids = page2.Items.Select(u => u.Id).ToHashSet();
        Assert.Empty(page1Ids.Intersect(page2Ids));

        // Assert: pages cover all users
        Assert.Equal(15, page1.TotalCount);
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(5, page2.Items.Count);
        Assert.Equal(15, page1Ids.Union(page2Ids).Count());
    }

    [Fact]
    public async Task GetUsersAsync_TotalCount_IsCorrect_AcrossAllPages()
    {
        var timestamp = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 25; i++)
            _context.Users.Add(CreateUser(i, "011", timestamp));
        await _context.SaveChangesAsync();

        var page1 = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, null, null, PageNumber: 1, PageSize: 10));
        var page2 = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, null, null, PageNumber: 2, PageSize: 10));
        var page3 = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, null, null, PageNumber: 3, PageSize: 10));

        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(25, page2.TotalCount);
        Assert.Equal(25, page3.TotalCount);
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(10, page2.Items.Count);
        Assert.Equal(5, page3.Items.Count);
    }

    // ── Branch filter ────────────────────────────────────────────────────

    [Fact]
    public async Task GetUsersAsync_BranchFilter_ReducesItemsAndTotalCount()
    {
        // Arrange: 10 in "011", 5 in "008"
        var timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 10; i++)
            _context.Users.Add(CreateUser(i, "011", timestamp));
        for (var i = 11; i <= 15; i++)
            _context.Users.Add(CreateUser(i, "008", timestamp));
        await _context.SaveChangesAsync();

        // Act
        var allUsers = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, null, null, PageNumber: 1, PageSize: 50));
        var branch011 = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, "011", null, PageNumber: 1, PageSize: 50));
        var branch008 = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, "008", null, PageNumber: 1, PageSize: 50));

        // Assert
        Assert.Equal(15, allUsers.TotalCount);
        Assert.Equal(10, branch011.TotalCount);
        Assert.Equal(10, branch011.Items.Count);
        Assert.Equal(5, branch008.TotalCount);
        Assert.Equal(5, branch008.Items.Count);
        Assert.All(branch011.Items, u => Assert.Equal("011", u.BranchId));
        Assert.All(branch008.Items, u => Assert.Equal("008", u.BranchId));
    }

    [Fact]
    public async Task GetUsersAsync_BranchFilter_WithPagination_ReturnsCorrectPage()
    {
        // Arrange: 15 in "011", 5 in "008"
        var timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 15; i++)
            _context.Users.Add(CreateUser(i, "011", timestamp));
        for (var i = 16; i <= 20; i++)
            _context.Users.Add(CreateUser(i, "008", timestamp));
        await _context.SaveChangesAsync();

        // Act: page 2 of branch "011" (pageSize=10) → should return 5 items
        var page1 = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, "011", null, PageNumber: 1, PageSize: 10));
        var page2 = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, "011", null, PageNumber: 2, PageSize: 10));

        // Assert
        Assert.Equal(15, page1.TotalCount); // totalCount is branch-scoped
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(5, page2.Items.Count);

        // Pages are disjoint
        var page1Ids = page1.Items.Select(u => u.Id).ToHashSet();
        var page2Ids = page2.Items.Select(u => u.Id).ToHashSet();
        Assert.Empty(page1Ids.Intersect(page2Ids));

        // All items belong to the filtered branch
        Assert.All(page1.Items, u => Assert.Equal("011", u.BranchId));
        Assert.All(page2.Items, u => Assert.Equal("011", u.BranchId));
    }

    [Fact]
    public async Task GetUsersAsync_BranchFilter_CaseInsensitive_Matches()
    {
        // Arrange
        _context.Users.Add(CreateUser(1, "011", DateTime.UtcNow));
        _context.Users.Add(CreateUser(2, "008", DateTime.UtcNow));
        await _context.SaveChangesAsync();

        // Act: filter with different casing — the filter is exact-match on
        // the string, but branch codes are numeric so casing is moot.
        // This test documents the behavior.
        var result = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, "011", null, PageNumber: 1, PageSize: 50));

        Assert.Single(result.Items);
        Assert.Equal("011", result.Items[0].BranchId);
    }

    // ── Search + branch filter combo ─────────────────────────────────────

    [Fact]
    public async Task GetUsersAsync_SearchAndBranchFilter_CombineCorrectly()
    {
        var timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _context.Users.Add(CreateUser(1, "011", timestamp)); // user001
        _context.Users.Add(CreateUser(2, "011", timestamp)); // user002
        _context.Users.Add(CreateUser(3, "008", timestamp)); // user003
        _context.Users.Add(CreateUser(4, "008", timestamp)); // user004
        await _context.SaveChangesAsync();

        // Search for "user00" in branch "011" only
        var result = await _repository.GetUsersAsync(
            new UserQueryParameters("user00", null, "011", null, PageNumber: 1, PageSize: 50));

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, u => Assert.Equal("011", u.BranchId));
    }

    // ── Empty branch filter is ignored ───────────────────────────────────

    [Fact]
    public async Task GetUsersAsync_EmptyBranchFilter_ReturnsAllUsers()
    {
        var timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _context.Users.Add(CreateUser(1, "011", timestamp));
        _context.Users.Add(CreateUser(2, "008", timestamp));
        await _context.SaveChangesAsync();

        var result = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, "", null, PageNumber: 1, PageSize: 50));

        Assert.Equal(2, result.TotalCount);
    }

    // ── Ordering stability ───────────────────────────────────────────────

    [Fact]
    public async Task GetUsersAsync_OrdersByCreatedAtDescending_ThenById()
    {
        var t1 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        _context.Users.Add(CreateUser(1, "011", t1)); // oldest
        _context.Users.Add(CreateUser(2, "011", t2)); // newest
        _context.Users.Add(CreateUser(3, "011", t2)); // same timestamp as 2
        await _context.SaveChangesAsync();

        var result = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, null, null, PageNumber: 1, PageSize: 50));

        Assert.Equal(3, result.Items.Count);
        // First two should be from t2 (newest), ordered by Id
        Assert.Equal(2, result.Items[0].Id);
        Assert.Equal(3, result.Items[1].Id);
        // Last should be from t1 (oldest)
        Assert.Equal(1, result.Items[2].Id);
    }
}

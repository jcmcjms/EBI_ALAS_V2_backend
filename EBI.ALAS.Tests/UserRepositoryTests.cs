using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Users;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace EBI.ALAS.Tests;
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
    [Fact]
    public async Task GetUsersAsync_PagesAreDisjoint_WhenCreatedAtTies()
    {
        var timestamp = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 15; i++)
            _context.Users.Add(CreateUser(i, "011", timestamp));
        await _context.SaveChangesAsync();
        var page1Params = new UserQueryParameters(null, null, null, null, PageNumber: 1, PageSize: 10);
        var page2Params = new UserQueryParameters(null, null, null, null, PageNumber: 2, PageSize: 10);
        var page1 = await _repository.GetUsersAsync(page1Params);
        var page2 = await _repository.GetUsersAsync(page2Params);
        var page1Ids = page1.Items.Select(u => u.Id).ToHashSet();
        var page2Ids = page2.Items.Select(u => u.Id).ToHashSet();
        Assert.Empty(page1Ids.Intersect(page2Ids));
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
    [Fact]
    public async Task GetUsersAsync_BranchFilter_ReducesItemsAndTotalCount()
    {
        var timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 10; i++)
            _context.Users.Add(CreateUser(i, "011", timestamp));
        for (var i = 11; i <= 15; i++)
            _context.Users.Add(CreateUser(i, "008", timestamp));
        await _context.SaveChangesAsync();
        var allUsers = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, null, null, PageNumber: 1, PageSize: 50));
        var branch011 = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, "011", null, PageNumber: 1, PageSize: 50));
        var branch008 = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, "008", null, PageNumber: 1, PageSize: 50));
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
        var timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 15; i++)
            _context.Users.Add(CreateUser(i, "011", timestamp));
        for (var i = 16; i <= 20; i++)
            _context.Users.Add(CreateUser(i, "008", timestamp));
        await _context.SaveChangesAsync();
        var page1 = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, "011", null, PageNumber: 1, PageSize: 10));
        var page2 = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, "011", null, PageNumber: 2, PageSize: 10));
        Assert.Equal(15, page1.TotalCount);
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(5, page2.Items.Count);
        var page1Ids = page1.Items.Select(u => u.Id).ToHashSet();
        var page2Ids = page2.Items.Select(u => u.Id).ToHashSet();
        Assert.Empty(page1Ids.Intersect(page2Ids));
        Assert.All(page1.Items, u => Assert.Equal("011", u.BranchId));
        Assert.All(page2.Items, u => Assert.Equal("011", u.BranchId));
    }
    [Fact]
    public async Task GetUsersAsync_BranchFilter_CaseInsensitive_Matches()
    {
        _context.Users.Add(CreateUser(1, "011", DateTime.UtcNow));
        _context.Users.Add(CreateUser(2, "008", DateTime.UtcNow));
        await _context.SaveChangesAsync();
        var result = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, "011", null, PageNumber: 1, PageSize: 50));
        Assert.Single(result.Items);
        Assert.Equal("011", result.Items[0].BranchId);
    }
    [Fact]
    public async Task GetUsersAsync_SearchAndBranchFilter_CombineCorrectly()
    {
        var timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _context.Users.Add(CreateUser(1, "011", timestamp));
        _context.Users.Add(CreateUser(2, "011", timestamp));
        _context.Users.Add(CreateUser(3, "008", timestamp));
        _context.Users.Add(CreateUser(4, "008", timestamp));
        await _context.SaveChangesAsync();
        var result = await _repository.GetUsersAsync(
            new UserQueryParameters("user00", null, "011", null, PageNumber: 1, PageSize: 50));
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, u => Assert.Equal("011", u.BranchId));
    }
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
    [Fact]
    public async Task GetUsersAsync_OrdersByCreatedAtDescending_ThenById()
    {
        var t1 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        _context.Users.Add(CreateUser(1, "011", t1));
        _context.Users.Add(CreateUser(2, "011", t2));
        _context.Users.Add(CreateUser(3, "011", t2));
        await _context.SaveChangesAsync();
        var result = await _repository.GetUsersAsync(
            new UserQueryParameters(null, null, null, null, PageNumber: 1, PageSize: 50));
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(2, result.Items[0].Id);
        Assert.Equal(3, result.Items[1].Id);
        Assert.Equal(1, result.Items[2].Id);
    }
}

using Ebi.Alas.Api.Composition.Pagination;
using Ebi.Alas.Api.Features.Users;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Users.ListUsers;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Users;

public sealed class ListUsersFilterTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"list-users-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static User Seed(
        string userName,
        string firstName,
        string lastName,
        string branchId,
        UserRole role,
        string middleName = "")
    {
        return User.Create(
            userName,
            "hash",
            firstName,
            lastName,
            middleName,
            $"{userName}@example.invalid",
            branchId,
            role,
            DateTimeOffset.UtcNow);
    }

    private static async Task<PageResult<UserResponse>> ListAsync(
        AlasDbContext db,
        string? search = null,
        string? role = null,
        string? branchId = null)
    {
        var handler = new ListUsersHandler(db);
        return await handler.HandleAsync(
            new PageRequest { Page = 1, PageSize = 50 },
            maxPageSize: 100,
            defaultPageSize: 20,
            search,
            role,
            branchId,
            CancellationToken.None);
    }

    [Fact]
    public async Task Search_FiltersByUsernameAndName()
    {
        await using var db = Db();
        db.Users.Add(Seed("ada.lovelace", "Ada", "Lovelace", "011", UserRole.Encoder));
        db.Users.Add(Seed("alan.turing", "Alan", "Turing", "011", UserRole.Encoder));
        db.Users.Add(Seed("grace.hopper", "Grace", "Hopper", "011", UserRole.Encoder));
        await db.SaveChangesAsync();

        var byUsername = await ListAsync(db, search: "ada.");
        Assert.Equal("ada.lovelace", Assert.Single(byUsername.Items).UserName);

        var byFirst = await ListAsync(db, search: "Alan");
        Assert.Equal("alan.turing", Assert.Single(byFirst.Items).UserName);

        var byLast = await ListAsync(db, search: "Hopper");
        Assert.Equal("grace.hopper", Assert.Single(byLast.Items).UserName);
    }

    [Fact]
    public async Task Role_FiltersByUserRole()
    {
        await using var db = Db();
        db.Users.Add(Seed("ada", "Ada", "Lovelace", "011", UserRole.Encoder));
        db.Users.Add(Seed("alan", "Alan", "Turing", "011", UserRole.Admin));
        await db.SaveChangesAsync();

        var page = await ListAsync(db, role: "Admin");

        Assert.Equal("alan", Assert.Single(page.Items).UserName);
    }

    [Fact]
    public async Task BranchId_FiltersByBranchCode()
    {
        await using var db = Db();
        db.Users.Add(Seed("ada", "Ada", "Lovelace", "011", UserRole.Encoder));
        db.Users.Add(Seed("alan", "Alan", "Turing", "022", UserRole.Encoder));
        await db.SaveChangesAsync();

        var page = await ListAsync(db, branchId: "022");

        Assert.Equal("alan", Assert.Single(page.Items).UserName);
    }

    [Fact]
    public async Task CombinedFilters_AndTogether()
    {
        await using var db = Db();
        db.Users.Add(Seed("ada", "Ada", "Lovelace", "011", UserRole.Encoder));
        db.Users.Add(Seed("ada.admin", "Ada", "Admin", "011", UserRole.Admin));
        db.Users.Add(Seed("alan", "Alan", "Turing", "022", UserRole.Encoder));
        await db.SaveChangesAsync();

        var page = await ListAsync(db, search: "Ada", role: "Encoder", branchId: "011");

        Assert.Equal("ada", Assert.Single(page.Items).UserName);
        Assert.Equal(1, page.TotalCount);
    }
}

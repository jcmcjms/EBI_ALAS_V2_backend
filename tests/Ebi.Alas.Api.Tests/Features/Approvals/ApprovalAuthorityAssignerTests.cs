using Ebi.Alas.Api.Features.Approvals;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Approvals;

public sealed class ApprovalAuthorityAssignerTests
{
    private static AlasDbContext Db() =>
        new(new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"assign-{Guid.NewGuid():N}")
            .Options);

    private static User Officer(string username, string? jobTitle, UserRole role = UserRole.Approver) =>
        User.Create(username, "hash", "F", username, "", $"{username}@x.com", "011",
            role, DateTimeOffset.UtcNow, jobTitle);

    private static async Task<Guid> AuthorityIdByKeyAsync(AlasDbContext db, string key) =>
        (await db.ApprovalAuthorities.AsNoTracking().SingleAsync(a => a.Key == key)).Id;

    [Fact]
    public async Task AssignAsync_MappedTitle_AssignsDefaultAuthority()
    {
        await using var db = Db();
        var user = Officer("ah", "Area Head");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await new ApprovalAuthorityAssigner(db).AssignAsync(user, CancellationToken.None);
        await db.SaveChangesAsync();

        var assignment = await db.ApprovalAuthorityUsers.AsNoTracking().SingleAsync();
        Assert.Equal(user.Id, assignment.UserId);
        Assert.Equal(await AuthorityIdByKeyAsync(db, "area-head"), assignment.ApprovalAuthorityId);
    }

    [Fact]
    public async Task AssignAsync_MissingAuthority_CreatesDefault()
    {
        await using var db = Db();
        var user = Officer("oic", "OIC");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await new ApprovalAuthorityAssigner(db).AssignAsync(user, CancellationToken.None);
        await db.SaveChangesAsync();

        var authority = await db.ApprovalAuthorities.AsNoTracking().SingleAsync(a => a.Key == "officer-in-charge");
        Assert.Equal(1, authority.Tier);
        Assert.Equal(300_000m, authority.MaxTotalExposure);
        var assignment = await db.ApprovalAuthorityUsers.AsNoTracking().SingleAsync();
        Assert.Equal(authority.Id, assignment.ApprovalAuthorityId);
    }

    [Fact]
    public async Task AssignAsync_ReplacesPriorAssignments()
    {
        await using var db = Db();
        var user = Officer("ph", "Product Head");
        var stale = ApprovalAuthority.Create("Custom Desk", 1, 0, true, true, 0, 10m, AuthorityScopes.Global);
        db.Users.Add(user);
        db.ApprovalAuthorities.Add(stale);
        await db.SaveChangesAsync();
        db.ApprovalAuthorityUsers.Add(new ApprovalAuthorityUser
        {
            ApprovalAuthorityId = stale.Id,
            UserId = user.Id,
        });
        await db.SaveChangesAsync();

        await new ApprovalAuthorityAssigner(db).AssignAsync(user, CancellationToken.None);
        await db.SaveChangesAsync();

        var assignment = await db.ApprovalAuthorityUsers.AsNoTracking().SingleAsync();
        Assert.Equal(await AuthorityIdByKeyAsync(db, "product-head"), assignment.ApprovalAuthorityId);
    }

    [Fact]
    public async Task AssignAsync_UnmappedTitle_LeavesUserUnassigned()
    {
        await using var db = Db();
        var user = Officer("ao", "Account Officer", UserRole.Encoder);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await new ApprovalAuthorityAssigner(db).AssignAsync(user, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Empty(await db.ApprovalAuthorityUsers.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task AssignAsync_BlankTitle_LeavesUserUnassigned()
    {
        await using var db = Db();
        var user = Officer("blank", null);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await new ApprovalAuthorityAssigner(db).AssignAsync(user, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Empty(await db.ApprovalAuthorityUsers.AsNoTracking().ToListAsync());
    }
}

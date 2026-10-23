using Ebi.Alas.Api.Features.Approvals;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Approvals;

public sealed class ApprovalAuthoritySeederTests
{
    private static AlasDbContext Db() =>
        new(new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"seed-{Guid.NewGuid():N}")
            .Options);

    private static User Approver(string username, string jobTitle) =>
        User.Create(
            username, "hash", "F", username, "", $"{username}@x.com", "011",
            UserRole.Approver, DateTimeOffset.UtcNow, jobTitle);

    [Fact]
    public async Task SeedAsync_EmptyCatalog_SeedsTheMatrixAuthorities()
    {
        await using var db = Db();

        var seeded = await new ApprovalAuthoritySeeder(db).SeedAsync(CancellationToken.None);

        Assert.True(seeded);
        var authorities = await db.ApprovalAuthorities.AsNoTracking().ToListAsync();
        Assert.Equal(9, authorities.Count);

        var branchHead = authorities.Single(a => a.DisplayName == "Branch Head");
        Assert.Equal(1, branchHead.Tier);
        Assert.Equal(300_000m, branchHead.MaxTotalExposure);
        Assert.Equal(0, branchHead.MaxSeverity);
        Assert.False(branchHead.AllowNew);
        Assert.True(branchHead.AllowRenewal);
        Assert.Equal(AuthorityScopes.Branch, branchHead.ScopeType);

        var officerInCharge = authorities.Single(a => a.DisplayName == "Officer In Charge");
        Assert.Equal(1, officerInCharge.Tier);
        Assert.Equal(300_000m, officerInCharge.MaxTotalExposure);
        Assert.Equal(0, officerInCharge.MaxSeverity);
        Assert.False(officerInCharge.AllowNew);
        Assert.True(officerInCharge.AllowRenewal);
        Assert.Equal(AuthorityScopes.Branch, officerInCharge.ScopeType);

        var areaHead = authorities.Single(a => a.DisplayName == "Area Head");
        Assert.Equal(2, areaHead.Tier);
        Assert.Equal(600_000m, areaHead.MaxTotalExposure);
        Assert.Equal(AuthorityScopes.Area, areaHead.ScopeType);

        var rbgHead = authorities.Single(a => a.DisplayName == "RBG Head");
        Assert.Equal(3, rbgHead.Tier);
        Assert.Equal(1_000_000m, rbgHead.MaxTotalExposure);
        Assert.Equal(1, rbgHead.MaxSeverity);
        Assert.True(rbgHead.AllowNew);
        Assert.Equal(AuthorityScopes.Global, rbgHead.ScopeType);

        var productHead = authorities.Single(a => a.DisplayName == "Product Head");
        Assert.Equal(3, productHead.Tier);
        Assert.Equal(1_000_000m, productHead.MaxTotalExposure);
        Assert.Equal(1, productHead.MaxSeverity);
        Assert.True(productHead.AllowNew);
        Assert.Equal(AuthorityScopes.Global, productHead.ScopeType);

        var creditHead = authorities.Single(a => a.DisplayName == "Credit Head");
        Assert.Equal(3, creditHead.Tier);
        Assert.Equal(1_000_000m, creditHead.MaxTotalExposure);
        Assert.Equal(1, creditHead.MaxSeverity);
        Assert.True(creditHead.AllowNew);
        Assert.Equal(AuthorityScopes.Global, creditHead.ScopeType);

        var coo = authorities.Single(a => a.DisplayName == "COO");
        Assert.Equal(4, coo.Tier);
        Assert.Equal(1_200_000m, coo.MaxTotalExposure);
        Assert.Equal(2, coo.MaxSeverity);
        Assert.Equal(AuthorityScopes.Global, coo.ScopeType);

        var president = authorities.Single(a => a.DisplayName == "President / CEO");
        Assert.Equal(5, president.Tier);
        Assert.Equal(1_500_000m, president.MaxTotalExposure);
        Assert.Equal(2, president.MaxSeverity);
        Assert.Equal(AuthorityScopes.Global, president.ScopeType);

        var creCom = authorities.Single(a => a.DisplayName == "CreCom / Board");
        Assert.Equal(6, creCom.Tier);
        Assert.Equal(10_000_000m, creCom.MaxTotalExposure);
        Assert.Equal(2, creCom.MaxSeverity);
        Assert.Equal(AuthorityScopes.Global, creCom.ScopeType);
    }

    [Fact]
    public async Task SeedAsync_AssignsApproversByJobTitle()
    {
        await using var db = Db();
        var branchHead = Approver("bh", "Branch Head");
        var officerInCharge = Approver("oic", "OIC");
        var areaHead = Approver("ah", "Area Head");
        var rbgHead = Approver("rbg", "RBG Head");
        var productHead = Approver("ph", "Product Head");
        var creditHead = Approver("ch", "Credit Head");
        var coo = Approver("coo", "COO");
        var president = Approver("pc", "President & CEO");
        db.Users.AddRange(branchHead, officerInCharge, areaHead, rbgHead, productHead, creditHead, coo, president);
        await db.SaveChangesAsync();

        await new ApprovalAuthoritySeeder(db).SeedAsync(CancellationToken.None);

        var assignments = await db.ApprovalAuthorityUsers.AsNoTracking().ToListAsync();
        var authorities = await db.ApprovalAuthorities.AsNoTracking().ToListAsync();

        Assert.Equal(8, assignments.Count);
        Assert.Equal(branchHead.Id, AssigneeOf(assignments, authorities, "Branch Head"));
        Assert.Equal(officerInCharge.Id, AssigneeOf(assignments, authorities, "Officer In Charge"));
        Assert.Equal(areaHead.Id, AssigneeOf(assignments, authorities, "Area Head"));
        Assert.Equal(rbgHead.Id, AssigneeOf(assignments, authorities, "RBG Head"));
        Assert.Equal(productHead.Id, AssigneeOf(assignments, authorities, "Product Head"));
        Assert.Equal(creditHead.Id, AssigneeOf(assignments, authorities, "Credit Head"));
        Assert.Equal(coo.Id, AssigneeOf(assignments, authorities, "COO"));
        Assert.Equal(president.Id, AssigneeOf(assignments, authorities, "President / CEO"));
    }

    [Fact]
    public async Task SeedAsync_ExistingCatalog_LeavesItAlone()
    {
        await using var db = Db();
        var existing = ApprovalAuthority.Create("Custom Desk", 1, 0, true, true, 0, 10m, AuthorityScopes.Global);
        db.ApprovalAuthorities.Add(existing);
        await db.SaveChangesAsync();

        var seeded = await new ApprovalAuthoritySeeder(db).SeedAsync(CancellationToken.None);

        Assert.False(seeded);
        Assert.Equal(1, await db.ApprovalAuthorities.AsNoTracking().CountAsync());
    }

    private static Guid AssigneeOf(
        List<ApprovalAuthorityUser> assignments,
        List<ApprovalAuthority> authorities,
        string displayName)
    {
        var authority = authorities.Single(a => a.DisplayName == displayName);
        return assignments.Single(a => a.ApprovalAuthorityId == authority.Id).UserId;
    }
}

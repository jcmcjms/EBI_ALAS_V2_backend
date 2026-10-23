using Ebi.Alas.Api.Features.Approvals;
using Ebi.Alas.Api.Features.Branches;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Approvals;

public sealed class ApprovalScopeServiceTests
{
    private static AlasDbContext Db() =>
        new(new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"scope-{Guid.NewGuid():N}")
            .Options);

    private static User Approver(string username, string branch = "011") =>
        User.Create(
            username, "hash", "F", username, "", $"{username}@x.com", branch,
            UserRole.Approver, DateTimeOffset.UtcNow, "Approver");

    private static async Task AssignAsync(AlasDbContext db, ApprovalAuthority authority, params User[] users)
    {
        db.ApprovalAuthorities.Add(authority);
        foreach (var user in users)
        {
            db.Users.Add(user);
            db.ApprovalAuthorityUsers.Add(new ApprovalAuthorityUser
            {
                ApprovalAuthorityId = authority.Id,
                UserId = user.Id,
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ResolveAsync_NoAuthoritiesConfigured_CatalogNotConfigured()
    {
        await using var db = Db();

        var resolution = await new ApprovalScopeService(db).ResolveAsync([Guid.NewGuid()], CancellationToken.None);

        Assert.False(resolution.CatalogConfigured);
    }

    [Fact]
    public async Task ResolveAsync_GlobalAuthority_HoldsGlobalScope()
    {
        await using var db = Db();
        db.Branches.Add(Branch.Create("011", "Main", "NA", DateTimeOffset.UtcNow));
        db.Branches.Add(Branch.Create("012", "Other", "NA", DateTimeOffset.UtcNow));
        var chief = Approver("chief");
        await AssignAsync(db, ApprovalAuthority.Create("Global Desk", 1, 0, true, true, 2, 5_000_000m, AuthorityScopes.Global), chief);

        var resolution = await new ApprovalScopeService(db).ResolveAsync([chief.Id], CancellationToken.None);

        Assert.True(resolution.CatalogConfigured);
        Assert.Equal(AuthorityScopes.Global, resolution.ScopeFor(chief.Id).ScopeType);
        Assert.True(await BranchVisibility.IsGlobalAuthorityAsync(db, chief.Id, "Approver", CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_SharedDesk_TwoMembersIsShared()
    {
        await using var db = Db();
        var first = Approver("first");
        var second = Approver("second");
        var desk = ApprovalAuthority.Create("Desk", 1, 0, true, true, 0, 1_000_000m, AuthorityScopes.Branch);
        await AssignAsync(db, desk, first, second);

        var resolution = await new ApprovalScopeService(db).ResolveAsync([first.Id, second.Id], CancellationToken.None);

        Assert.True(resolution.Scopes.ContainsKey(first.Id));
        Assert.True(resolution.ScopeFor(first.Id).SharedDesk);
        Assert.True(resolution.ScopeFor(second.Id).SharedDesk);
    }

    [Fact]
    public async Task ResolveAsync_SingleMemberDesk_IsNotShared()
    {
        await using var db = Db();
        var only = Approver("only");
        await AssignAsync(db, ApprovalAuthority.Create("Desk", 1, 0, true, true, 0, 1_000_000m, AuthorityScopes.Branch), only);

        var resolution = await new ApprovalScopeService(db).ResolveAsync([only.Id], CancellationToken.None);

        Assert.False(resolution.ScopeFor(only.Id).SharedDesk);
    }

    [Fact]
    public async Task ResolveAsync_UnassignedOfficer_KeepsBranchScope()
    {
        await using var db = Db();
        var assigned = Approver("assigned");
        var unassigned = Approver("unassigned");
        await AssignAsync(db, ApprovalAuthority.Create("Desk", 1, 0, true, true, 0, 1_000_000m, AuthorityScopes.Global), assigned);
        db.Users.Add(unassigned);
        await db.SaveChangesAsync();

        var resolution = await new ApprovalScopeService(db).ResolveAsync([unassigned.Id], CancellationToken.None);

        Assert.True(resolution.CatalogConfigured);
        Assert.Equal(AuthorityScopes.Branch, resolution.ScopeFor(unassigned.Id).ScopeType);
        Assert.False(await BranchVisibility.IsGlobalAuthorityAsync(db, unassigned.Id, "Approver", CancellationToken.None));
    }

    [Fact]
    public async Task GetReadableBranchIdsAsync_AreaScope_ReturnsAreaBranches()
    {
        await using var db = Db();
        db.Branches.Add(Branch.Create("011", "Main", "NA", DateTimeOffset.UtcNow));
        db.Branches.Add(Branch.Create("012", "Sibling", "NA", DateTimeOffset.UtcNow));
        db.Branches.Add(Branch.Create("021", "Elsewhere", "VIS", DateTimeOffset.UtcNow));
        var chief = Approver("chief");
        await AssignAsync(db, ApprovalAuthority.Create("Area Desk", 1, 0, true, true, 0, 1_000_000m, AuthorityScopes.Area), chief);

        var readable = await BranchVisibility.GetReadableBranchIdsAsync(
            db, chief.Id, UserRole.Approver, "011", CancellationToken.None, "Approver");

        Assert.Contains("011", readable);
        Assert.Contains("012", readable);
        Assert.DoesNotContain("021", readable);
    }
}

using Ebi.Alas.Api.Features.Branches;

namespace Ebi.Alas.Api.Tests.Features.Branches;

public sealed class BranchDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_TrimsAndDefaultsToActive()
    {
        var branch = Branch.Create(" 011 ", " Main Branch ", " NA ", Now);
        Assert.Equal("011", branch.Code);
        Assert.Equal("Main Branch", branch.Name);
        Assert.Equal("NA", branch.AreaCode);
        Assert.Equal(BranchStatus.Active, branch.Status);
        Assert.Equal(Now, branch.CreatedAt);
        Assert.Equal(Now, branch.UpdatedAt);
    }

    [Fact]
    public void Create_RejectsBlankCode()
    {
        Assert.Throws<ArgumentException>(() => Branch.Create(" ", "Name", "NA", Now));
    }

    [Fact]
    public void Create_RejectsBlankAreaCode()
    {
        Assert.Throws<ArgumentException>(() => Branch.Create("011", "Name", " ", Now));
    }

    [Fact]
    public void UpdateDetails_ChangesNameAndArea_KeepsCode()
    {
        var branch = Branch.Create("011", "Main", "NA", Now);
        var later = Now.AddMinutes(5);
        branch.UpdateDetails("Main Branch Renamed", "MINDANAO", later);
        Assert.Equal("011", branch.Code);
        Assert.Equal("Main Branch Renamed", branch.Name);
        Assert.Equal("MINDANAO", branch.AreaCode);
        Assert.Equal(later, branch.UpdatedAt);
    }

    [Fact]
    public void ChangeStatus_UpdatesStatusAndTimestamp()
    {
        var branch = Branch.Create("011", "Main", "NA", Now);
        var later = Now.AddHours(1);
        branch.ChangeStatus(BranchStatus.Closed, later);
        Assert.Equal(BranchStatus.Closed, branch.Status);
        Assert.Equal(later, branch.UpdatedAt);
    }
}

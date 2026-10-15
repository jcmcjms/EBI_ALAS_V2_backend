using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.WebLoans;

namespace Ebi.Alas.Api.Tests.Features.WebLoans;

public sealed class WebLoanAccessTests
{
    private static CisDetail Detail(params string[] branchCodes) =>
        new(
            new CisBorrower(
                "CIS-1", "Ana", null, "Cruz", null, null, null, null,
                null, null, null, null, null, null, null, null, null, null),
            branchCodes
                .Select((b, i) => new CisAccount(
                    "EBI", b, $"{i:0000}", $"{b}-{i:0000}", "Acc", 0m, 0m, "P"))
                .ToList());

    [Fact]
    public void Encoder_OnlySeesOwnBranchAccounts()
    {
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");
        var scoped = WebLoanAccess.FilterAccountsForCaller(Detail("BR-A", "BR-B"), caller);

        Assert.Single(scoped.Accounts);
        Assert.Equal("BR-A", scoped.Accounts[0].BranchCode);
    }

    [Fact]
    public void Admin_SeesAllBranchAccounts()
    {
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Admin, "BR-A");
        var scoped = WebLoanAccess.FilterAccountsForCaller(Detail("BR-A", "BR-B"), caller);

        Assert.Equal(2, scoped.Accounts.Count);
    }

    [Fact]
    public void CanUseAccount_DeniesOtherBranch_ForEncoder()
    {
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");
        Assert.False(WebLoanAccess.CanUseAccount(caller, "BR-B"));
        Assert.True(WebLoanAccess.CanUseAccount(caller, "BR-A"));
    }
}

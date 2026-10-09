using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Users.ImportUsers;

namespace Ebi.Alas.Api.Tests.Features.Users.ImportUsers;

public sealed class ImportRoleParsingTests
{
    [Theory]
    [InlineData("Encoder", UserRole.Encoder)]
    [InlineData("encoder", UserRole.Encoder)]
    [InlineData("  Admin  ", UserRole.Admin)]
    [InlineData("Administrator", UserRole.Admin)]
    [InlineData("Admin (Administrator)", UserRole.Admin)]
    [InlineData("Approver (Area Head)", UserRole.Approver)]
    [InlineData("Recommender", UserRole.Recommender)]
    [InlineData("Evaluator", UserRole.Evaluator)]
    public void TryParseRole_AcceptsCatalogAndDisplayNames(string raw, UserRole expected)
    {
        Assert.True(ImportUsersHandler.TryParseRole(raw, out var role));
        Assert.Equal(expected, role);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("System")]
    [InlineData("Nope")]
    [InlineData("1")]
    public void TryParseRole_RejectsInvalidOrSystem(string? raw)
    {
        Assert.False(ImportUsersHandler.TryParseRole(raw, out _));
    }
}

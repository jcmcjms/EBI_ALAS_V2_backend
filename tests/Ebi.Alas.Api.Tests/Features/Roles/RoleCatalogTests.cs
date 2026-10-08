using Ebi.Alas.Api.Features.Roles;
using Ebi.Alas.Api.Features.Users.Domain;

namespace Ebi.Alas.Api.Tests.Features.Roles;

public sealed class RoleCatalogTests
{
    [Fact]
    public void Admin_IncludesLoansView_SoDashboardRouteGuardPasses()
    {
        var admin = RoleCatalog.All.Single(r => r.Role == nameof(UserRole.Admin));

        Assert.Contains("loans.view", admin.Permissions);
    }

    [Fact]
    public void Encoder_IncludesLoansView()
    {
        var encoder = RoleCatalog.All.Single(r => r.Role == nameof(UserRole.Encoder));

        Assert.Contains("loans.view", encoder.Permissions);
    }
}

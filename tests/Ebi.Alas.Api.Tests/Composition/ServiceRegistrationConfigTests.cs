using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ebi.Alas.Api.Tests.Composition;

public sealed class ServiceRegistrationConfigTests
{
    private sealed class ProductionFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            TestHostSecrets.Apply(builder, useEmptyConnectionStrings: true);
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Alas", "");
            builder.UseSetting("ConnectionStrings:WebLoan", "");
        }
    }

    [Fact]
    public void MissingConnectionString_InProduction_ThrowsAtStartup()
    {
        using var factory = new ProductionFactory();
        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var current = ex;
        var found = false;
        while (current is not null)
        {
            if (current.Message.Contains("ConnectionStrings:Alas", StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                break;
            }

            current = current.InnerException;
        }

        Assert.True(found, "Expected startup failure mentioning ConnectionStrings:Alas");
    }
}

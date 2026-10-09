using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Ebi.Alas.Api.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF tools. Prefer env var, then user secrets,
/// then LocalDB fallback so `dotnet ef` matches the running app's Alas DB.
/// </summary>
public sealed class AlasDbContextFactory : IDesignTimeDbContextFactory<AlasDbContext>
{
    public AlasDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AlasDbContext>();
        builder.UseSqlServer(ResolveConnectionString());
        return new AlasDbContext(builder.Options);
    }

    private static string ResolveConnectionString()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Alas");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets(typeof(AlasDbContextFactory).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        return config.GetConnectionString("Alas")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=ALASv2_DB;Trusted_Connection=True;TrustServerCertificate=True";
    }
}

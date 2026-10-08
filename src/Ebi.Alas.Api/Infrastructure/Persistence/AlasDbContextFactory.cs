using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ebi.Alas.Api.Infrastructure.Persistence;

public sealed class AlasDbContextFactory : IDesignTimeDbContextFactory<AlasDbContext>
{
    public AlasDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AlasDbContext>();
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Alas")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=ALASv2_DB;Trusted_Connection=True;TrustServerCertificate=True";
        builder.UseSqlServer(cs);
        return new AlasDbContext(builder.Options);
    }
}

using Alas.Domain.Entities;
using Alas.Infrastructure.Auditing;
using Alas.Infrastructure.Identity;
using Alas.Infrastructure.Loans;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Alas.Infrastructure.Persistence;

public class AlasDbContext : IdentityDbContext<AppUser, AppRole, Guid>
{
    public AlasDbContext(DbContextOptions<AlasDbContext> options) : base(options)
    {
    }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Loan> Loans => Set<Loan>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("identity");

        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(AlasDbContext).Assembly);

        builder.Entity<AppUser>(entity =>
        {
            entity.Property(e => e.FullName)
                .HasMaxLength(200);

            entity.Property(e => e.PermissionVersion)
                .HasDefaultValue(0);
        });

        builder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshToken");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.TokenHash)
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(e => e.IpAddress)
                .HasMaxLength(100);

            entity.Property(e => e.UserAgent)
                .HasMaxLength(500);

            entity.Property(e => e.RevokedReason)
                .HasMaxLength(200);

            entity.HasIndex(e => e.TokenHash)
                .IsUnique();

            entity.HasIndex(e => e.UserId);

            entity.Ignore(e => e.IsRevoked);
            entity.Ignore(e => e.IsExpired);
            entity.Ignore(e => e.IsActive);
        });

        builder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs", "audit");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Action)
                .HasMaxLength(200)
                .IsRequired();
        });

        builder.ApplyConfigurationsFromAssembly(typeof(LoanEntityConfiguration).Assembly);
    }
}

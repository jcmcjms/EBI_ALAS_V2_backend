using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Branches;
using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Infrastructure.Data;

/// <summary>
/// Application database context for EBI.ALAS.V2 banking API.
/// Configures all entity relationships, indexes, and constraints.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // ─── DbSets ──────────────────────────────────────────────────────────────
    public DbSet<User> Users => Set<User>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<LoanApplication> LoanApplications => Set<LoanApplication>();
    public DbSet<LoanAction> LoanActions => Set<LoanAction>();
    public DbSet<OutstandingLoan> OutstandingLoans => Set<OutstandingLoan>();
    public DbSet<BuyOut> BuyOuts => Set<BuyOut>();
    public DbSet<RevokedToken> RevokedTokens => Set<RevokedToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ─── Branch Entity ─────────────────────────────────────────────────
        modelBuilder.Entity<Branch>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.Code)
                .IsRequired()
                .HasMaxLength(20);

            entity.HasIndex(e => e.Code)
                .IsUnique();

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            entity.Property(e => e.CreatedAt)
                .IsRequired();
        });

        // ─── User Entity ─────────────────────────────────────────────────
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.Username)
                .IsRequired()
                .HasMaxLength(50);

            entity.HasIndex(e => e.Username)
                .IsUnique();

            entity.Property(e => e.PasswordHash)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.FirstName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.MiddleName)
                .HasMaxLength(100);

            entity.Property(e => e.LastName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.BranchId)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.Role)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            entity.Property(e => e.CreatedAt)
                .IsRequired();
        });

        // ─── LoanApplication Entity ──────────────────────────────────────
        modelBuilder.Entity<LoanApplication>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.FormNumber)
                .IsRequired()
                .HasMaxLength(30);

            entity.HasIndex(e => e.FormNumber)
                .IsUnique();

            entity.Property(e => e.BranchCode)
                .IsRequired()
                .HasMaxLength(20);

            // Client Information
            entity.Property(e => e.CisId)
                .HasMaxLength(50);

            entity.Property(e => e.FirstName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.MiddleName)
                .HasMaxLength(100);

            entity.Property(e => e.LastName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Agency)
                .HasMaxLength(100);

            entity.Property(e => e.Position)
                .HasMaxLength(100);

            entity.Property(e => e.EmployeeId)
                .HasMaxLength(50);

            entity.Property(e => e.NetTakeHomePay)
                .HasColumnType("decimal(18,2)");

            // Loan Parameters
            entity.Property(e => e.Product)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Purpose)
                .HasMaxLength(500);

            entity.Property(e => e.ProposedAmount)
                .IsRequired()
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.TermMonths)
                .IsRequired();

            entity.Property(e => e.InterestRate)
                .IsRequired()
                .HasColumnType("decimal(5,2)");

            entity.Property(e => e.ModeOfPayment)
                .HasMaxLength(50);

            entity.Property(e => e.CoMaker)
                .HasMaxLength(200);

            // Status & Audit
            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("Draft");

            entity.Property(e => e.ApplicationDate)
                .IsRequired();

            entity.Property(e => e.LastActionDate)
                .IsRequired();

            entity.Property(e => e.CreatedById)
                .IsRequired();

            // Foreign Key to User
            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── LoanAction Entity ───────────────────────────────────────────
        modelBuilder.Entity<LoanAction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.Action)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.FromStatus)
                .HasMaxLength(50);

            entity.Property(e => e.ToStatus)
                .HasMaxLength(50);

            entity.Property(e => e.Comments)
                .HasMaxLength(1000);

            entity.Property(e => e.ActionDate)
                .IsRequired();

            // Foreign Keys
            entity.HasOne(e => e.LoanApplication)
                .WithMany(l => l.Actions)
                .HasForeignKey(e => e.LoanApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ActionByUser)
                .WithMany()
                .HasForeignKey(e => e.ActionByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── OutstandingLoan Entity ──────────────────────────────────────
        modelBuilder.Entity<OutstandingLoan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.CreditorName)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.MonthlyPayment)
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.Balance)
                .HasColumnType("decimal(18,2)");

            // Foreign Key
            entity.HasOne(e => e.LoanApplication)
                .WithMany(l => l.OutstandingLoans)
                .HasForeignKey(e => e.LoanApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── BuyOut Entity ───────────────────────────────────────────────
        modelBuilder.Entity<BuyOut>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.CreditorName)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.Amount)
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.MonthlyAmortization)
                .HasColumnType("decimal(18,2)");

            // Foreign Key
            entity.HasOne(e => e.LoanApplication)
                .WithMany(l => l.BuyOuts)
                .HasForeignKey(e => e.LoanApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── RevokedToken Entity ─────────────────────────────────────────
        modelBuilder.Entity<RevokedToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.TokenId)
                .IsRequired()
                .HasMaxLength(200);

            entity.HasIndex(e => e.TokenId)
                .IsUnique();

            entity.Property(e => e.ExpiresAt)
                .IsRequired();
        });

        // ─── RefreshToken Entity ────────────────────────────────────────
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.TokenHash)
                .IsRequired()
                .HasMaxLength(128); // SHA-256 hex = 64 chars, generous limit

            entity.HasIndex(e => e.TokenHash)
                .IsUnique();

            entity.Property(e => e.UserId)
                .IsRequired();

            entity.Property(e => e.DeviceInfo)
                .HasMaxLength(500);

            entity.Property(e => e.CreatedAt)
                .IsRequired();

            entity.Property(e => e.ExpiresAt)
                .IsRequired();

            entity.Property(e => e.AbsoluteExpiry)
                .IsRequired();

            entity.Property(e => e.IsRevoked)
                .IsRequired()
                .HasDefaultValue(false);

            // Cascade: deleting a user deletes their refresh tokens
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

using EBI.ALAS.Api.Features.Auth;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Username)
            .IsRequired()
            .HasMaxLength(50);
        builder.HasIndex(e => e.Username)
            .IsUnique();
        builder.Property(e => e.PasswordHash)
            .IsRequired()
            .HasMaxLength(256);
        builder.Property(e => e.FirstName)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(e => e.MiddleName)
            .HasMaxLength(100);
        builder.Property(e => e.LastName)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(e => e.BranchId)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(e => e.Role)
            .IsRequired()
            .HasMaxLength(50);
        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);
        builder.Property(e => e.MustChangePassword)
            .IsRequired()
            .HasDefaultValue(false);
        builder.Property(e => e.CreatedAt)
            .IsRequired();
        builder.Property(e => e.Email)
            .HasMaxLength(100);
        builder.Property(e => e.Phone)
            .HasMaxLength(20);
        builder.Property(e => e.EmergencyContact)
            .HasMaxLength(200);
        builder.Property(e => e.ProfilePhotoUrl)
            .HasMaxLength(500);
        builder.Property(e => e.PasswordChangedAt);
        builder.Property(e => e.TempPasswordExpiresAt);
        builder.Property(e => e.JobTitle)
            .HasMaxLength(100);
        builder.Property(e => e.ESignature)
            .HasMaxLength(2000000);
        builder.Property(e => e.ApprovalAuthorityKey)
            .HasMaxLength(50);
        builder.HasIndex(e => e.ApprovalAuthorityKey)
            .HasDatabaseName("IX_Users_ApprovalAuthorityKey");
        builder.HasIndex(e => new { e.Role, e.BranchId, e.IsActive })
            .HasDatabaseName("IX_Users_Role_BranchId_IsActive");
        builder.HasIndex(e => new { e.IsActive, e.ApprovalAuthorityKey, e.Role })
            .HasDatabaseName("IX_Users_IsActive_ApprovalAuthorityKey_Role");
        builder.HasOne(e => e.ApprovalAuthority)
            .WithMany()
            .HasForeignKey(e => e.ApprovalAuthorityKey)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

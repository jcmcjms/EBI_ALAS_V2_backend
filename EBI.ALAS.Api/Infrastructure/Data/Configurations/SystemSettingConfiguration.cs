using EBI.ALAS.Api.Features.SystemSettings;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;
public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(e => e.Value)
            .IsRequired()
            .HasMaxLength(500);
        builder.Property(e => e.UpdatedAt)
            .IsRequired();
        builder.Property(e => e.UpdatedById)
            .IsRequired();
        builder.HasOne(e => e.UpdatedBy)
            .WithMany()
            .HasForeignKey(e => e.UpdatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

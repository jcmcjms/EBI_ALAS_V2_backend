using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;
public class WorkflowQueueItemConfiguration : IEntityTypeConfiguration<WorkflowQueueItem>
{
    public void Configure(EntityTypeBuilder<WorkflowQueueItem> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Stage)
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.Property(e => e.State)
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.Property(e => e.PartitionKey)
            .IsRequired()
            .HasMaxLength(64);
        builder.Property(e => e.EnqueuedAt)
            .IsRequired();
        builder.HasIndex(e => new { e.LoanApplicationId, e.Stage })
            .IsUnique()
            .HasFilter("[State] IN ('Queued','Active')")
            .HasDatabaseName("IX_WorkflowQueueItems_Loan_Stage_Live");
        builder.HasIndex(e => new { e.PartitionKey, e.State, e.EnqueuedAt, e.Id })
            .HasDatabaseName("IX_WorkflowQueueItems_Partition_Head");
        builder.HasIndex(e => new { e.PartitionKey, e.State })
            .IsUnique()
            .HasFilter("[State] = 'Active'")
            .HasDatabaseName("IX_WorkflowQueueItems_Partition_Active_Unique");
        builder.HasIndex(e => new { e.Stage, e.State, e.PartitionKey, e.EnqueuedAt, e.Id })
            .HasDatabaseName("IX_WorkflowQueueItems_Stage_State_Partition");
        builder.HasIndex(e => new { e.State, e.OwnerUserId })
            .HasDatabaseName("IX_WorkflowQueueItems_State_OwnerUserId");
        builder.HasIndex(e => new { e.OwnerUserId, e.LeasedAt })
            .HasFilter("[OwnerUserId] IS NOT NULL")
            .HasDatabaseName("IX_WorkflowQueueItems_Owner_LeasedAt");
        builder.HasOne(e => e.LoanApplication)
            .WithMany()
            .HasForeignKey(e => e.LoanApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.OwnerUser)
            .WithMany()
            .HasForeignKey(e => e.OwnerUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

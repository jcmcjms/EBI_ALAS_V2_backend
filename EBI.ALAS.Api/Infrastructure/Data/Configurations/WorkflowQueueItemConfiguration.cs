using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

// Materialized-head FIFO desk. One live row (Queued|Active) per
// (loan, stage); completed rows remain for audit.
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

        // One live queue row per (loan, stage); completed rows remain for audit.
        // SQL Server filtered unique index — prevents double-enqueue at DB level.
        builder.HasIndex(e => new { e.LoanApplicationId, e.Stage })
            .IsUnique()
            .HasFilter("[State] IN ('Queued','Active')")
            .HasDatabaseName("IX_WorkflowQueueItems_Loan_Stage_Live");

        // Head lookup + rank scan per desk.
        builder.HasIndex(e => new { e.PartitionKey, e.State, e.EnqueuedAt, e.Id })
            .HasDatabaseName("IX_WorkflowQueueItems_Partition_Head");

        // Enforce one Active item per partition at the DB level.
        // Without this, concurrent PromoteAsync calls can create two Active
        // items in the same desk, breaking the FIFO invariant.
        builder.HasIndex(e => new { e.PartitionKey, e.State })
            .IsUnique()
            .HasFilter("[State] = 'Active'")
            .HasDatabaseName("IX_WorkflowQueueItems_Partition_Active_Unique");

        // Dashboard: document completion queue (Stage + State filter, ordered by PartitionKey/EnqueuedAt/Id)
        builder.HasIndex(e => new { e.Stage, e.State, e.PartitionKey, e.EnqueuedAt, e.Id })
            .HasDatabaseName("IX_WorkflowQueueItems_Stage_State_Partition");

        // ResolveOwnerAsync: active load per candidate (State + OwnerUserId)
        builder.HasIndex(e => new { e.State, e.OwnerUserId })
            .HasDatabaseName("IX_WorkflowQueueItems_State_OwnerUserId");

        // FK to LoanApplication. Cascade — deleting a loan cleans its queue rows.
        builder.HasOne(e => e.LoanApplication)
            .WithMany()
            .HasForeignKey(e => e.LoanApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK to User (owner). SetNull — deleting a user releases the desk slot.
        builder.HasOne(e => e.OwnerUser)
            .WithMany()
            .HasForeignKey(e => e.OwnerUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

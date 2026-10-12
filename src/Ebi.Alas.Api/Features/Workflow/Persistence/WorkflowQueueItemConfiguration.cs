using Ebi.Alas.Api.Features.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebi.Alas.Api.Features.Workflow.Persistence;

public sealed class WorkflowQueueItemConfiguration : IEntityTypeConfiguration<WorkflowQueueItem>
{
    public void Configure(EntityTypeBuilder<WorkflowQueueItem> builder)
    {
        builder.ToTable("WorkflowQueueItems");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PartitionKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Stage).HasConversion<int>();
        builder.Property(x => x.State).HasConversion<int>();
        // Application-managed row version: incremented by domain mutators and enforced
        // with a WHERE Version = original clause on update. Works on SQL Server and Sqlite.
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.Stage, x.PartitionKey, x.State, x.Sequence });
        builder.HasIndex(x => x.LoanApplicationId);
    }
}

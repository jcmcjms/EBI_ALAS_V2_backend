using EBI.ALAS.Api.Common.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EBI.ALAS.Api.Infrastructure.Interceptors;

/// <summary>
/// EF Core interceptor that automatically sets CreatedAt and UpdatedAt timestamps
/// on entities that implement the IAuditable interface.
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyAuditInfo(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditInfo(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void ApplyAuditInfo(DbContext? context)
    {
        if (context == null) return;

        var utcNow = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
            {
                // Set CreatedAt for new entities
                if (entry.Properties.Any(p => p.Metadata.Name == "CreatedAt"))
                {
                    entry.Property("CreatedAt").CurrentValue = utcNow;
                }
            }

            if (entry.State == EntityState.Modified)
            {
                // Set UpdatedAt for modified entities (if the property exists)
                if (entry.Properties.Any(p => p.Metadata.Name == "UpdatedAt"))
                {
                    entry.Property("UpdatedAt").CurrentValue = utcNow;
                }

                // Prevent modification of CreatedAt
                if (entry.Properties.Any(p => p.Metadata.Name == "CreatedAt"))
                {
                    entry.Property("CreatedAt").IsModified = false;
                }
            }
        }
    }
}

/// <summary>
/// Interface for entities that support audit tracking.
/// </summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
}

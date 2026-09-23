using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

// ALAS-owned mirror of webloan.loan_product. The PK is the
// webloan id_code (string) so the sync upsert is idempotent by
// natural key. IsRetired is derived from webloan's
// `expiration IS NOT NULL` at sync time — no second source of
// truth, no manual toggle.
public class LoanProductConfiguration : IEntityTypeConfiguration<LoanProduct>
{
    public void Configure(EntityTypeBuilder<LoanProduct> builder)
    {
        builder.HasKey(e => e.Code);
        builder.Property(e => e.Code)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.Description)
            .IsRequired()
            .HasMaxLength(200);

        // Eligibility bounds — column types chosen to match the
        // existing decimal conventions in LoanApplication so the
        // disbursement math is homogeneous.
        builder.Property(e => e.MinAmount)
            .IsRequired()
            .HasColumnType("decimal(18,2)");
        builder.Property(e => e.MaxAmount)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(e => e.MinTermDays).IsRequired();
        builder.Property(e => e.MaxTermDays).IsRequired();

        // Fees — all PHP, all decimal(18,2) for consistency with
        // proposed-amount.
        builder.Property(e => e.NotarialFee)
            .IsRequired()
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0m);
        builder.Property(e => e.DocStampFee)
            .IsRequired()
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0m);
        builder.Property(e => e.InsuranceFee)
            .IsRequired()
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0m);

        // decimal(9,6) supports up to 999.999999% — comfortably
        // above the realistic 0.05–0.30 range. 6 fractional digits
        // is enough precision for a per-annum rate; over-precise
        // values would imply false accuracy to ops.
        builder.Property(e => e.AdvanceInterestRate)
            .IsRequired()
            .HasColumnType("decimal(9,6)")
            .HasDefaultValue(0m);

        // Sync state. IsRetired defaults to true so a brand-new
        // mirror row inserted with default policy values is hidden
        // from the dropdown until the first successful sync run
        // proves it actually exists in webloan AND has a
        // non-retired state. Prevents "phantom" products from
        // appearing in encoders' dropdowns if the sync has never
        // run.
        builder.Property(e => e.IsRetired)
            .IsRequired()
            .HasDefaultValue(true);
        builder.Property(e => e.LastSyncedAt).IsRequired();

        // Audit (UpdatedDate / UpdatedById)
        // UpdatedDate is datetime2 to match every other timestamp
        // column in the schema (LoanApplication.LastActionDate,
        // User.CreatedAt, AuditLog.Timestamp). Required because
        // the repository sets it on every successful
        // SaveChangesAsync — the application, not the DB, owns the
        // value, so no SQL DEFAULT.
        builder.Property(e => e.UpdatedDate)
            .IsRequired();

        // UpdatedById is a nullable int FK to User.Id. Nullable
        // because the background sync service inserts/updates rows
        // with no human attribution. On admin-driven updates the
        // endpoint layer passes the caller's user id so the column
        // is non-null at that point.
        builder.Property(e => e.UpdatedById);

        // Index on UpdatedById so the admin "show rows modified by
        // user X" filter pattern (and any future per-modifier
        // audit query) does a single seek. Single-column index —
        // a composite is unnecessary because UpdatedById is
        // already selective on its own (each user only touches a
        // handful of products), and EF can combine the index with
        // the PK seek if the predicate also narrows on Code.
        builder.HasIndex(e => e.UpdatedById)
            .HasDatabaseName("IX_LoanProducts_UpdatedById");

        // FK to User. Restrict (not Cascade) mirrors
        // LoanApplication.CreatedById — deleting a user must
        // never silently rewrite the audit history by nulling the
        // modifier on every row they touched. SetNull was the
        // alternative but it would erase attribution; an admin
        // who wants to retire a user with historical edits has to
        // first reassign or soft-delete the affected rows.
        builder.HasOne(e => e.UpdatedBy)
            .WithMany()
            .HasForeignKey(e => e.UpdatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

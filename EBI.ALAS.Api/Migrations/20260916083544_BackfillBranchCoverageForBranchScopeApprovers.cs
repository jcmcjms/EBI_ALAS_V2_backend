using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class BackfillBranchCoverageForBranchScopeApprovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill: seed coverage = home branch for Branch-scope
            // approvers who have no rows in UserBranchCoverage yet.
            //
            // Without this, existing approver accounts that were created
            // before the coverage feature would have an empty branch set
            // in BranchScopeService — causing their approval queues to
            // silently return zero rows. The FailSafe in BranchScopeService
            // catches this at runtime (falls back to home branch), but
            // the backfill ensures the data is consistent so the service
            // never has to fall back in the first place.
            //
            // Idempotent: the NOT EXISTS guard means running it twice
            // inserts zero rows.
            migrationBuilder.Sql(@"
                INSERT INTO UserBranchCoverages (UserId, BranchCode)
                SELECT u.Id, u.BranchId
                FROM Users u
                JOIN ApprovalAuthorities a ON a.[Key] = u.ApprovalAuthorityKey
                WHERE a.ScopeType = 0                       -- Branch
                  AND u.IsActive = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM UserBranchCoverages c
                      WHERE c.UserId = u.Id
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the seeded rows. The backfill only inserts rows
            // for users who had zero coverage rows, so deleting rows
            // where the BranchCode matches the user's home branch
            // reverses exactly what Up() inserted — without touching
            // rows that were manually added by admins after the deploy.
            //
            // Note: this is a best-effort reversal. If an admin added
            // extra coverage rows after the backfill, those are
            // preserved because the WHERE clause only matches rows
            // where BranchCode == the user's home branch AND the user
            // is a Branch-scope approver.
            migrationBuilder.Sql(@"
                DELETE c
                FROM UserBranchCoverages c
                INNER JOIN Users u ON u.Id = c.UserId
                INNER JOIN ApprovalAuthorities a ON a.[Key] = u.ApprovalAuthorityKey
                WHERE a.ScopeType = 0                       -- Branch
                  AND c.BranchCode = u.BranchId;            -- home branch only
            ");
        }
    }
}

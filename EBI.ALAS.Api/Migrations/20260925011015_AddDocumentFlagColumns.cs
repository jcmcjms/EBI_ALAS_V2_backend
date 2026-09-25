using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentFlagColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Step 1: Un-hold live files (uses only existing columns) ──
            // Move every live ForIncompleteDocuments file back to its
            // stored return desk (the desk it was flagged from).
            migrationBuilder.Sql("""
                UPDATE LoanApplications
                SET Status = ISNULL(IncompleteReturnStatus, 'ForChecking'),
                    IncompleteReturnStatus = NULL
                WHERE Status = 'ForIncompleteDocuments'
                """);

            // ── Step 2: Drop orphan DOC queue rows ─────────────────────
            migrationBuilder.Sql("""
                UPDATE wq SET DequeuedAt = SYSUTCDATETIME(), State = 'Released'
                FROM WorkflowQueueItems wq
                JOIN LoanApplications l ON l.Id = wq.LoanApplicationId
                WHERE wq.State = 'Active' AND wq.PartitionKey LIKE 'DOC:%'
                """);

            // ── Step 3: Add new columns (must exist before backfill) ───
            migrationBuilder.AddColumn<string>(
                name: "DocumentFlagReason",
                table: "LoanApplications",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DocumentsFlaggedAt",
                table: "LoanApplications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DocumentsFlaggedById",
                table: "LoanApplications",
                type: "int",
                nullable: true);

            // ── Step 4: Backfill flag facts from the audit trail ────────
            // Preserve the flag data from LoanActions so nothing is lost
            // when we drop IncompleteReturnStatus.
            migrationBuilder.Sql("""
                UPDATE l SET
                    l.DocumentsFlaggedAt  = a.ActionDate,
                    l.DocumentsFlaggedById = a.ActionByUserId,
                    l.DocumentFlagReason  = a.Comments
                FROM LoanApplications l
                CROSS APPLY (
                    SELECT TOP 1 ActionDate, ActionByUserId, Comments
                    FROM LoanActions
                    WHERE LoanApplicationId = l.Id AND ToStatus = 'ForIncompleteDocuments'
                    ORDER BY ActionDate DESC
                ) a
                WHERE l.DocumentsFlaggedAt IS NULL
                  AND EXISTS (SELECT 1 FROM DocumentChecklists d
                              WHERE d.LoanApplicationId = l.Id AND d.Status IN ('Missing','Pending'))
                """);

            // ── Step 5: Drop old column ────────────────────────────────
            migrationBuilder.DropColumn(
                name: "IncompleteReturnStatus",
                table: "LoanApplications");

            // ── Step 6: Indexes and FK ─────────────────────────────────
            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_DocumentsFlaggedAt",
                table: "LoanApplications",
                column: "DocumentsFlaggedAt",
                filter: "[DocumentsFlaggedAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_DocumentsFlaggedById",
                table: "LoanApplications",
                column: "DocumentsFlaggedById");

            migrationBuilder.AddForeignKey(
                name: "FK_LoanApplications_Users_DocumentsFlaggedById",
                table: "LoanApplications",
                column: "DocumentsFlaggedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LoanApplications_Users_DocumentsFlaggedById",
                table: "LoanApplications");

            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_DocumentsFlaggedAt",
                table: "LoanApplications");

            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_DocumentsFlaggedById",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "DocumentFlagReason",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "DocumentsFlaggedAt",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "DocumentsFlaggedById",
                table: "LoanApplications");

            migrationBuilder.AddColumn<string>(
                name: "IncompleteReturnStatus",
                table: "LoanApplications",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }
    }
}

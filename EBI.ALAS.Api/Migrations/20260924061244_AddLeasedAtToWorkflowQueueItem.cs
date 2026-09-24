using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLeasedAtToWorkflowQueueItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowQueueItems_OwnerUserId",
                table: "WorkflowQueueItems");

            migrationBuilder.AddColumn<DateTime>(
                name: "LeasedAt",
                table: "WorkflowQueueItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowQueueItems_Owner_LeasedAt",
                table: "WorkflowQueueItems",
                columns: new[] { "OwnerUserId", "LeasedAt" },
                filter: "[OwnerUserId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowQueueItems_Owner_LeasedAt",
                table: "WorkflowQueueItems");

            migrationBuilder.DropColumn(
                name: "LeasedAt",
                table: "WorkflowQueueItems");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowQueueItems_OwnerUserId",
                table: "WorkflowQueueItems",
                column: "OwnerUserId");
        }
    }
}

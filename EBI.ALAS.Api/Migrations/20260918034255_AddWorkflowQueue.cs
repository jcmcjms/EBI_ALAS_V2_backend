using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowQueueItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoanApplicationId = table.Column<int>(type: "int", nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PartitionKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EnqueuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OwnerUserId = table.Column<int>(type: "int", nullable: true),
                    PromotedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DequeuedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowQueueItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowQueueItems_LoanApplications_LoanApplicationId",
                        column: x => x.LoanApplicationId,
                        principalTable: "LoanApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowQueueItems_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowQueueItems_Loan_Stage_Live",
                table: "WorkflowQueueItems",
                columns: new[] { "LoanApplicationId", "Stage" },
                unique: true,
                filter: "[State] IN ('Queued','Active')");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowQueueItems_OwnerUserId",
                table: "WorkflowQueueItems",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowQueueItems_Partition_Head",
                table: "WorkflowQueueItems",
                columns: new[] { "PartitionKey", "State", "EnqueuedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowQueueItems");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LoanActions_ActionByUserId",
                table: "LoanActions");

            migrationBuilder.RenameIndex(
                name: "IX_LoanActions_LoanApplicationId",
                table: "LoanActions",
                newName: "IX_LoanActions_LoanId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowQueueItems_Partition_Active_Unique",
                table: "WorkflowQueueItems",
                columns: new[] { "PartitionKey", "State" },
                unique: true,
                filter: "[State] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowQueueItems_Stage_State_Partition",
                table: "WorkflowQueueItems",
                columns: new[] { "Stage", "State", "PartitionKey", "EnqueuedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowQueueItems_State_OwnerUserId",
                table: "WorkflowQueueItems",
                columns: new[] { "State", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_IsActive_ApprovalAuthorityKey_Role",
                table: "Users",
                columns: new[] { "IsActive", "ApprovalAuthorityKey", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Role_BranchId_IsActive",
                table: "Users",
                columns: new[] { "Role", "BranchId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_Branch_ApplicationDate",
                table: "LoanApplications",
                columns: new[] { "BranchCode", "ApplicationDate" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_BranchCode_Status_ApplicationDate",
                table: "LoanApplications",
                columns: new[] { "BranchCode", "Status", "ApplicationDate" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_CreatedById_ApplicationDate",
                table: "LoanApplications",
                columns: new[] { "CreatedById", "ApplicationDate" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_Status_Assigned_Tier",
                table: "LoanApplications",
                columns: new[] { "Status", "AssignedApproverId", "RequiredApprovalTier" });

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_Status_LastAction",
                table: "LoanApplications",
                columns: new[] { "Status", "LastActionDate" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_LoanActions_ActionBy_ActionDate",
                table: "LoanActions",
                columns: new[] { "ActionByUserId", "ActionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LoanActions_Loan_ApplicationDate",
                table: "LoanActions",
                columns: new[] { "LoanApplicationId", "ActionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LoanActions_ToStatus_ActionDate",
                table: "LoanActions",
                columns: new[] { "ToStatus", "ActionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChecklists_Loan_Status_Code",
                table: "DocumentChecklists",
                columns: new[] { "LoanApplicationId", "Status", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_Branches_AreaCode",
                table: "Branches",
                column: "AreaCode");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalAuthorities_Tier_Priority",
                table: "ApprovalAuthorities",
                columns: new[] { "Tier", "Priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowQueueItems_Partition_Active_Unique",
                table: "WorkflowQueueItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowQueueItems_Stage_State_Partition",
                table: "WorkflowQueueItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowQueueItems_State_OwnerUserId",
                table: "WorkflowQueueItems");

            migrationBuilder.DropIndex(
                name: "IX_Users_IsActive_ApprovalAuthorityKey_Role",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Role_BranchId_IsActive",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_Branch_ApplicationDate",
                table: "LoanApplications");

            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_BranchCode_Status_ApplicationDate",
                table: "LoanApplications");

            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_CreatedById_ApplicationDate",
                table: "LoanApplications");

            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_Status_Assigned_Tier",
                table: "LoanApplications");

            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_Status_LastAction",
                table: "LoanApplications");

            migrationBuilder.DropIndex(
                name: "IX_LoanActions_ActionBy_ActionDate",
                table: "LoanActions");

            migrationBuilder.DropIndex(
                name: "IX_LoanActions_Loan_ApplicationDate",
                table: "LoanActions");

            migrationBuilder.DropIndex(
                name: "IX_LoanActions_ToStatus_ActionDate",
                table: "LoanActions");

            migrationBuilder.DropIndex(
                name: "IX_DocumentChecklists_Loan_Status_Code",
                table: "DocumentChecklists");

            migrationBuilder.DropIndex(
                name: "IX_Branches_AreaCode",
                table: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalAuthorities_Tier_Priority",
                table: "ApprovalAuthorities");

            migrationBuilder.RenameIndex(
                name: "IX_LoanActions_LoanId",
                table: "LoanActions",
                newName: "IX_LoanActions_LoanApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanActions_ActionByUserId",
                table: "LoanActions",
                column: "ActionByUserId");
        }
    }
}

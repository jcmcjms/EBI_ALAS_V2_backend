using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace EBI.ALAS.Api.Migrations
{
    public partial class AddUserApprovalAuthorityNavigation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_AssignedApproverId",
                table: "LoanApplications",
                column: "AssignedApproverId");
            migrationBuilder.AddForeignKey(
                name: "FK_LoanApplications_Users_AssignedApproverId",
                table: "LoanApplications",
                column: "AssignedApproverId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
            migrationBuilder.AddForeignKey(
                name: "FK_Users_ApprovalAuthorities_ApprovalAuthorityKey",
                table: "Users",
                column: "ApprovalAuthorityKey",
                principalTable: "ApprovalAuthorities",
                principalColumn: "Key",
                onDelete: ReferentialAction.SetNull);
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LoanApplications_Users_AssignedApproverId",
                table: "LoanApplications");
            migrationBuilder.DropForeignKey(
                name: "FK_Users_ApprovalAuthorities_ApprovalAuthorityKey",
                table: "Users");
            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_AssignedApproverId",
                table: "LoanApplications");
        }
    }
}

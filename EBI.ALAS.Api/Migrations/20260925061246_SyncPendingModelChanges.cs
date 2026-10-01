using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace EBI.ALAS.Api.Migrations
{
    public partial class SyncPendingModelChanges : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LoanApplications_Users_DocumentsFlaggedById",
                table: "LoanApplications");
            migrationBuilder.AddForeignKey(
                name: "FK_LoanApplications_Users_DocumentsFlaggedById",
                table: "LoanApplications",
                column: "DocumentsFlaggedById",
                principalTable: "Users",
                principalColumn: "Id");
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LoanApplications_Users_DocumentsFlaggedById",
                table: "LoanApplications");
            migrationBuilder.AddForeignKey(
                name: "FK_LoanApplications_Users_DocumentsFlaggedById",
                table: "LoanApplications",
                column: "DocumentsFlaggedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}

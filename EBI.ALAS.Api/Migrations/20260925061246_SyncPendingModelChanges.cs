using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class SyncPendingModelChanges : Migration
    {
        /// <inheritdoc />
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

        /// <inheritdoc />
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

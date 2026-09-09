using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class RenameFormNumberColumnToLamId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FormNumber",
                table: "LoanApplications",
                newName: "LamId");

            migrationBuilder.RenameIndex(
                name: "IX_LoanApplications_FormNumber",
                table: "LoanApplications",
                newName: "IX_LoanApplications_LamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LamId",
                table: "LoanApplications",
                newName: "FormNumber");

            migrationBuilder.RenameIndex(
                name: "IX_LoanApplications_LamId",
                table: "LoanApplications",
                newName: "IX_LoanApplications_FormNumber");
        }
    }
}

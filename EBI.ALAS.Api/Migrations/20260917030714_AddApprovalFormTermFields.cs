using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalFormTermFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AnnualRatePercent",
                table: "LoanApplications",
                type: "decimal(9,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovalTermDays",
                table: "LoanApplications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PolicyTermMonths",
                table: "LoanApplications",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnnualRatePercent",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "ApprovalTermDays",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "PolicyTermMonths",
                table: "LoanApplications");
        }
    }
}

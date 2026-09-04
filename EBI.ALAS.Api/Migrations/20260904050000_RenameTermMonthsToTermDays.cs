using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class RenameTermMonthsToTermDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert legacy months values to days (1 month ≈ 30 days).
            // Conservative multiplicative conversion: rounds down. The
            // admin can reconfigure per-product bounds after the rename.
            migrationBuilder.Sql("UPDATE [LoanApplications] SET [TermMonths] = [TermMonths] * 30 WHERE [TermMonths] > 0");
            migrationBuilder.Sql("UPDATE [LoanProducts] SET [MinTermMonths] = [MinTermMonths] * 30, [MaxTermMonths] = [MaxTermMonths] * 30 WHERE [MaxTermMonths] > 0");

            migrationBuilder.RenameColumn(
                name: "TermMonths",
                table: "LoanApplications",
                newName: "TermDays");

            migrationBuilder.RenameColumn(
                name: "MinTermMonths",
                table: "LoanProducts",
                newName: "MinTermDays");

            migrationBuilder.RenameColumn(
                name: "MaxTermMonths",
                table: "LoanProducts",
                newName: "MaxTermDays");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MaxTermDays",
                table: "LoanProducts",
                newName: "MaxTermMonths");

            migrationBuilder.RenameColumn(
                name: "MinTermDays",
                table: "LoanProducts",
                newName: "MinTermMonths");

            migrationBuilder.RenameColumn(
                name: "TermDays",
                table: "LoanApplications",
                newName: "TermMonths");

            // Reverse the conversion: divide by 30.
            migrationBuilder.Sql("UPDATE [LoanApplications] SET [TermMonths] = [TermMonths] / 30 WHERE [TermMonths] > 0");
            migrationBuilder.Sql("UPDATE [LoanProducts] SET [MinTermMonths] = [MinTermMonths] / 30, [MaxTermMonths] = [MaxTermMonths] / 30 WHERE [MaxTermMonths] > 0");
        }
    }
}
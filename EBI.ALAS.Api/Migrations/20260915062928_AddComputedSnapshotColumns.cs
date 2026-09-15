using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddComputedSnapshotColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AmortizationMode",
                table: "LoanProducts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ApplicationChargeRate",
                table: "LoanProducts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "ChargeAdvanceInterest",
                table: "LoanProducts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AmortizationExceedsDisposable",
                table: "LoanApplications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "CapacityDeductions",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DeductionRate",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossDisposableIncome",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossProceeds",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaximumLoanableAmount",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyAmortization",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetDisposableIncome",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetPayAfterDeduction",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetProceedsOnDS",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetProceedsToClient",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "NthpBelowMinimum",
                table: "LoanApplications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardAdvanceInterest",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardApplicationCharge",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalDeductions",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalExposure",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AmortizationMode",
                table: "LoanProducts");

            migrationBuilder.DropColumn(
                name: "ApplicationChargeRate",
                table: "LoanProducts");

            migrationBuilder.DropColumn(
                name: "ChargeAdvanceInterest",
                table: "LoanProducts");

            migrationBuilder.DropColumn(
                name: "AmortizationExceedsDisposable",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "CapacityDeductions",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "DeductionRate",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "GrossDisposableIncome",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "GrossProceeds",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "MaximumLoanableAmount",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "MonthlyAmortization",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "NetDisposableIncome",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "NetPayAfterDeduction",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "NetProceedsOnDS",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "NetProceedsToClient",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "NthpBelowMinimum",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "StandardAdvanceInterest",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "StandardApplicationCharge",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "TotalDeductions",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "TotalExposure",
                table: "LoanApplications");
        }
    }
}

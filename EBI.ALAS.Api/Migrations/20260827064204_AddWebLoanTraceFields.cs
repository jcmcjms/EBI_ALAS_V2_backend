using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWebLoanTraceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WebLoanAccountNumbers",
                table: "LoanApplications",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WebLoanBranchCode",
                table: "LoanApplications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebLoanCisNo",
                table: "LoanApplications",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WebLoanLastSyncedAt",
                table: "LoanApplications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebLoanPnNumbers",
                table: "LoanApplications",
                type: "jsonb",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WebLoanAccountNumbers",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "WebLoanBranchCode",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "WebLoanCisNo",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "WebLoanLastSyncedAt",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "WebLoanPnNumbers",
                table: "LoanApplications");
        }
    }
}

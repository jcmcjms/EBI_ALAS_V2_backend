using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanProductsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_CreatedById",
                table: "LoanApplications");

            migrationBuilder.CreateTable(
                name: "LoanProducts",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MinAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MinTermMonths = table.Column<int>(type: "int", nullable: false),
                    MaxTermMonths = table.Column<int>(type: "int", nullable: false),
                    NotarialFee = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    DocStampFee = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    InsuranceFee = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    AdvanceInterestRate = table.Column<decimal>(type: "decimal(9,6)", nullable: false, defaultValue: 0m),
                    IsRetired = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoanProducts", x => x.Code);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_CreatedById_Status",
                table: "LoanApplications",
                columns: new[] { "CreatedById", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_Status_BranchCode",
                table: "LoanApplications",
                columns: new[] { "Status", "BranchCode" });

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_Status_BranchCode_Date",
                table: "LoanApplications",
                columns: new[] { "Status", "BranchCode", "ApplicationDate" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoanProducts");

            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_CreatedById_Status",
                table: "LoanApplications");

            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_Status_BranchCode",
                table: "LoanApplications");

            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_Status_BranchCode_Date",
                table: "LoanApplications");

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_CreatedById",
                table: "LoanApplications",
                column: "CreatedById");
        }
    }
}

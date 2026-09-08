using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class MultiLoanSubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Balance",
                table: "OutstandingLoans");

            migrationBuilder.DropColumn(
                name: "CreditorName",
                table: "OutstandingLoans");

            migrationBuilder.DropColumn(
                name: "MonthlyPayment",
                table: "OutstandingLoans");

            migrationBuilder.DropColumn(
                name: "MonthlyAmortization",
                table: "BuyOuts");

            migrationBuilder.RenameColumn(
                name: "CreditorName",
                table: "BuyOuts",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "Amount",
                table: "BuyOuts",
                newName: "OutstandingBalance");

            migrationBuilder.AddColumn<decimal>(
                name: "Amortization",
                table: "OutstandingLoans",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DateGranted",
                table: "OutstandingLoans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DateMaturity",
                table: "OutstandingLoans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OutstandingBalance",
                table: "OutstandingLoans",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Pn",
                table: "OutstandingLoans",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "PrincipalBalance",
                table: "OutstandingLoans",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ProductWithDescription",
                table: "OutstandingLoans",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "OutstandingLoans",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<decimal>(
                name: "InterestRate",
                table: "LoanApplications",
                type: "decimal(9,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "LoanApplications",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AoRecommendation",
                table: "LoanApplications",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationGroupNo",
                table: "LoanApplications",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "Birthdate",
                table: "LoanApplications",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreationTypeCode",
                table: "LoanApplications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreationTypeLabel",
                table: "LoanApplications",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviationDetails",
                table: "LoanApplications",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DeviationJustifications",
                table: "LoanApplications",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DivisionCode",
                table: "LoanApplications",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DocStamps",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "FeeDeviationJustification",
                table: "LoanApplications",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasDeviations",
                table: "LoanApplications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Insurance",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Lai",
                table: "LoanApplications",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LengthOfService",
                table: "LoanApplications",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoanNo",
                table: "LoanApplications",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MisAgency",
                table: "LoanApplications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NotarialFee",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "NthpDate",
                table: "LoanApplications",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OtherRemarks",
                table: "LoanApplications",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreLoanFormNumber",
                table: "LoanApplications",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreLoanId",
                table: "LoanApplications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductCode",
                table: "LoanApplications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "LoanApplications",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Remarks",
                table: "LoanApplications",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestingOfficer",
                table: "LoanApplications",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardDocStamps",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardInsurance",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardNotarialFee",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "StationCode",
                table: "LoanApplications",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Suffix",
                table: "LoanApplications",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationFindings",
                table: "LoanApplications",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Amortization",
                table: "BuyOuts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Pn",
                table: "BuyOuts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "EbiReloans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoanApplicationId = table.Column<int>(type: "int", nullable: false),
                    Pn = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExistingDeduction = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OutstandingBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PayToClose = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EbiReloans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EbiReloans_LoanApplications_LoanApplicationId",
                        column: x => x.LoanApplicationId,
                        principalTable: "LoanApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IncomingLoans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoanApplicationId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Deductions = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingLoans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncomingLoans_LoanApplications_LoanApplicationId",
                        column: x => x.LoanApplicationId,
                        principalTable: "LoanApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoanSubmissionIdempotencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoanSubmissionIdempotencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoanSubmissionIdempotencies_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_ApplicationGroupNo",
                table: "LoanApplications",
                column: "ApplicationGroupNo");

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_LoanNo",
                table: "LoanApplications",
                column: "LoanNo");

            migrationBuilder.CreateIndex(
                name: "IX_EbiReloans_LoanApplicationId",
                table: "EbiReloans",
                column: "LoanApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_IncomingLoans_LoanApplicationId",
                table: "IncomingLoans",
                column: "LoanApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanSubmissionIdempotencies_UserId",
                table: "LoanSubmissionIdempotencies",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanSubmissionIdempotency_Key_User",
                table: "LoanSubmissionIdempotencies",
                columns: new[] { "IdempotencyKey", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EbiReloans");

            migrationBuilder.DropTable(
                name: "IncomingLoans");

            migrationBuilder.DropTable(
                name: "LoanSubmissionIdempotencies");

            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_ApplicationGroupNo",
                table: "LoanApplications");

            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_LoanNo",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "Amortization",
                table: "OutstandingLoans");

            migrationBuilder.DropColumn(
                name: "DateGranted",
                table: "OutstandingLoans");

            migrationBuilder.DropColumn(
                name: "DateMaturity",
                table: "OutstandingLoans");

            migrationBuilder.DropColumn(
                name: "OutstandingBalance",
                table: "OutstandingLoans");

            migrationBuilder.DropColumn(
                name: "Pn",
                table: "OutstandingLoans");

            migrationBuilder.DropColumn(
                name: "PrincipalBalance",
                table: "OutstandingLoans");

            migrationBuilder.DropColumn(
                name: "ProductWithDescription",
                table: "OutstandingLoans");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "OutstandingLoans");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "AoRecommendation",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "ApplicationGroupNo",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "Birthdate",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "CreationTypeCode",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "CreationTypeLabel",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "DeviationDetails",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "DeviationJustifications",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "DivisionCode",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "DocStamps",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "FeeDeviationJustification",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "HasDeviations",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "Insurance",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "Lai",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "LengthOfService",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "LoanNo",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "MisAgency",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "NotarialFee",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "NthpDate",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "OtherRemarks",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "PreLoanFormNumber",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "PreLoanId",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "ProductCode",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "Remarks",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "RequestingOfficer",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "StandardDocStamps",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "StandardInsurance",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "StandardNotarialFee",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "StationCode",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "Suffix",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "VerificationFindings",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "Amortization",
                table: "BuyOuts");

            migrationBuilder.DropColumn(
                name: "Pn",
                table: "BuyOuts");

            migrationBuilder.RenameColumn(
                name: "OutstandingBalance",
                table: "BuyOuts",
                newName: "Amount");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "BuyOuts",
                newName: "CreditorName");

            migrationBuilder.AddColumn<decimal>(
                name: "Balance",
                table: "OutstandingLoans",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreditorName",
                table: "OutstandingLoans",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyPayment",
                table: "OutstandingLoans",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "InterestRate",
                table: "LoanApplications",
                type: "decimal(5,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(9,6)");

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyAmortization",
                table: "BuyOuts",
                type: "decimal(18,2)",
                nullable: true);
        }
    }
}

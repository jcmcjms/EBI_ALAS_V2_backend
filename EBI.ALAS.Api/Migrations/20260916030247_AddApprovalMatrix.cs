using System;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace EBI.ALAS.Api.Migrations
{
    public partial class AddApprovalMatrix : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalAuthorityKey",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
            migrationBuilder.AddColumn<int>(
                name: "AssignedApproverId",
                table: "LoanApplications",
                type: "int",
                nullable: true);
            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAt",
                table: "LoanApplications",
                type: "datetime2",
                nullable: true);
            migrationBuilder.AddColumn<int>(
                name: "DeviationSeverity",
                table: "LoanApplications",
                type: "int",
                nullable: false,
                defaultValue: 0);
            migrationBuilder.AddColumn<DateTime>(
                name: "DocumentsCompleteAt",
                table: "LoanApplications",
                type: "datetime2",
                nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "LoanType",
                table: "LoanApplications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "New");
            migrationBuilder.AddColumn<int>(
                name: "RequiredApprovalTier",
                table: "LoanApplications",
                type: "int",
                nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "AreaCode",
                table: "Branches",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);
            migrationBuilder.CreateTable(
                name: "ApprovalAuthorities",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    AllowNew = table.Column<bool>(type: "bit", nullable: false),
                    AllowRenewal = table.Column<bool>(type: "bit", nullable: false),
                    MaxSeverity = table.Column<int>(type: "int", nullable: false),
                    MaxTotalExposure = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ScopeType = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalAuthorities", x => x.Key);
                });
            migrationBuilder.CreateTable(
                name: "DeviationCatalog",
                columns: table => new
                {
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviationCatalog", x => x.Description);
                });
            migrationBuilder.CreateIndex(
                name: "IX_Users_ApprovalAuthorityKey",
                table: "Users",
                column: "ApprovalAuthorityKey");
            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_RoutingQueue",
                table: "LoanApplications",
                columns: new[] { "Status", "RequiredApprovalTier", "AssignedApproverId" });
            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_Status_AssignedApprover",
                table: "LoanApplications",
                columns: new[] { "Status", "AssignedApproverId" });
            migrationBuilder.InsertData(
                table: "ApprovalAuthorities",
                columns: new[] { "Key", "DisplayName", "Tier", "Priority", "AllowNew", "AllowRenewal", "MaxSeverity", "MaxTotalExposure", "ScopeType" },
                values: new object[,]
                {
                    { "BranchHead", "Branch Head", 1, 1, false, true, 0, 300000m, 0 },
                    { "OICLevel1", "OIC Level 1", 1, 2, false, true, 0, 300000m, 0 },
                    { "AreaHead", "Area Head", 2, 1, false, true, 0, 600000m, 1 },
                    { "RBGHead", "RBG Head", 3, 1, true, true, 1, 1000000m, 2 },
                    { "ProductHead", "Product Head", 3, 2, true, true, 1, 1000000m, 2 },
                    { "CreditHead", "Credit Head", 3, 3, true, true, 1, 1000000m, 2 },
                    { "COO", "Chief Operating Officer", 4, 1, true, true, 2, 1200000m, 2 },
                    { "CEO", "Chief Executive Officer", 5, 1, true, true, 2, 1500000m, 2 },
                    { "President", "President", 5, 2, true, true, 2, 1500000m, 2 },
                    { "CreComChair", "CreCom Chair Level D", 5, 3, true, true, 2, 1500000m, 2 },
                });
            migrationBuilder.InsertData(
                table: "DeviationCatalog",
                columns: new[] { "Description", "Severity" },
                values: new object[,]
                {
                    { "Age not within the prescribed parameters", 2 },
                    { "Discounted Application Fee", 2 },
                    { "Interest rate reduction", 2 },
                    { "With past due account - non performing loan", 2 },
                    { "Lacking bank statement of account", 1 },
                    { "Lacking CIBI", 1 },
                    { "Lacking marriage cert. with surname as single", 1 },
                    { "Lacking one or two payslip(s) for new atm loan", 1 },
                    { "Lacking signature in application form", 1 },
                    { "Lacking SPAs to claim ATM", 1 },
                    { "No appointment record and/or service record", 1 },
                    { "No FI SOA and loan ledger", 1 },
                    { "No latest payslip", 1 },
                    { "No interview sheet", 1 },
                    { "No orientation form or old form submitted", 1 },
                    { "No valid identification cards", 1 },
                    { "Total consumer loan exposure exceeding 1.2 million", 1 },
                    { "With blocked ATIM in same school", 1 },
                    { "With history of delinquency in the latest loan availment", 1 },
                    { "With NFIS findings", 1 },
                    { "With past due account - performing", 1 },
                });
            migrationBuilder.Sql("UPDATE Branches SET AreaCode = 'A1' WHERE Code IN ('003','008','012','023','025','026','028','029')");
            migrationBuilder.Sql("UPDATE Branches SET AreaCode = 'A2' WHERE Code IN ('000','002','004','005','007','009','017','027')");
            migrationBuilder.Sql("UPDATE Branches SET AreaCode = 'A3' WHERE Code IN ('006','011','014','015','016','022','024')");
            migrationBuilder.Sql("UPDATE Branches SET AreaCode = 'A4' WHERE Code IN ('013','019','020','021','030','031')");
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ApprovalAuthorities",
                keyColumn: "Key",
                keyValues: new object[]
                {
                    "BranchHead", "OICLevel1", "AreaHead", "RBGHead",
                    "ProductHead", "CreditHead", "COO", "CEO",
                    "President", "CreComChair"
                });
            migrationBuilder.DeleteData(
                table: "DeviationCatalog",
                keyColumn: "Description",
                keyValues: new object[]
                {
                    "Age not within the prescribed parameters",
                    "Discounted Application Fee",
                    "Interest rate reduction",
                    "With past due account - non performing loan",
                    "Lacking bank statement of account",
                    "Lacking CIBI",
                    "Lacking marriage cert. with surname as single",
                    "Lacking one or two payslip(s) for new atm loan",
                    "Lacking signature in application form",
                    "Lacking SPAs to claim ATM",
                    "No appointment record and/or service record",
                    "No FI SOA and loan ledger",
                    "No latest payslip",
                    "No interview sheet",
                    "No orientation form or old form submitted",
                    "No valid identification cards",
                    "Total consumer loan exposure exceeding 1.2 million",
                    "With blocked ATIM in same school",
                    "With history of delinquency in the latest loan availment",
                    "With NFIS findings",
                    "With past due account - performing",
                });
            migrationBuilder.Sql("UPDATE Branches SET AreaCode = NULL");
            migrationBuilder.DropTable(
                name: "ApprovalAuthorities");
            migrationBuilder.DropTable(
                name: "DeviationCatalog");
            migrationBuilder.DropIndex(
                name: "IX_Users_ApprovalAuthorityKey",
                table: "Users");
            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_RoutingQueue",
                table: "LoanApplications");
            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_Status_AssignedApprover",
                table: "LoanApplications");
            migrationBuilder.DropColumn(
                name: "ApprovalAuthorityKey",
                table: "Users");
            migrationBuilder.DropColumn(
                name: "AssignedApproverId",
                table: "LoanApplications");
            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "LoanApplications");
            migrationBuilder.DropColumn(
                name: "DeviationSeverity",
                table: "LoanApplications");
            migrationBuilder.DropColumn(
                name: "DocumentsCompleteAt",
                table: "LoanApplications");
            migrationBuilder.DropColumn(
                name: "LoanType",
                table: "LoanApplications");
            migrationBuilder.DropColumn(
                name: "RequiredApprovalTier",
                table: "LoanApplications");
            migrationBuilder.DropColumn(
                name: "AreaCode",
                table: "Branches");
        }
    }
}

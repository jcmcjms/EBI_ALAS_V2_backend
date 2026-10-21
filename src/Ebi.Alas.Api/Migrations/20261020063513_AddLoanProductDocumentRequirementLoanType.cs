using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebi.Alas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanProductDocumentRequirementLoanType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LoanProductDocumentRequirements_ProductCode_Code",
                table: "LoanProductDocumentRequirements");

            migrationBuilder.AddColumn<int>(
                name: "LoanType",
                table: "LoanProductDocumentRequirements",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_LoanProductDocumentRequirements_ProductCode_LoanType_Code",
                table: "LoanProductDocumentRequirements",
                columns: new[] { "ProductCode", "LoanType", "Code" },
                unique: true);

            // Replace legacy product-only catalog with the approved loan-type matrix.
            migrationBuilder.Sql("DELETE FROM LoanProductDocumentRequirements;");

            migrationBuilder.Sql(@"
INSERT INTO LoanProductDocumentRequirements (Id, ProductCode, LoanType, Code, Description) VALUES
(NEWID(), 'A16', 1, 'A1004', 'Duly Accomplished Application Form'),
(NEWID(), 'A16', 1, 'CCR38', 'CASH FLOW'),
(NEWID(), 'A16', 1, 'A2016', 'Confirmation From DEPED Verifier'),
(NEWID(), 'A16', 1, 'A2008', 'Proof of Income, Payslip, COE, Remittance Receipt'),
(NEWID(), 'A16', 1, 'PIC03', 'CLIENT SELFIE WITH ID'),
(NEWID(), 'A16', 1, 'A2020', 'CIBI Report'),
(NEWID(), 'A17', 1, 'A1004', 'Duly Accomplished Application Form'),
(NEWID(), 'A17', 1, 'CCR38', 'CASH FLOW'),
(NEWID(), 'A17', 1, 'A2016', 'Confirmation From DEPED Verifier'),
(NEWID(), 'A17', 1, 'A2008', 'Proof of Income, Payslip, COE, Remittance Receipt'),
(NEWID(), 'A17', 1, 'PIC03', 'CLIENT SELFIE WITH ID'),
(NEWID(), 'A17', 1, 'A2020', 'CIBI Report');
");

            migrationBuilder.Sql(@"
INSERT INTO LoanProductDocumentRequirements (Id, ProductCode, LoanType, Code, Description) VALUES
(NEWID(), 'A16', 2, 'CCR38', 'CASH FLOW'),
(NEWID(), 'A16', 2, 'A2016', 'Confirmation From DEPED Verifier'),
(NEWID(), 'A16', 2, 'A2008', 'Proof of Income, Payslip, COE, Remittance Receipt'),
(NEWID(), 'A17', 2, 'CCR38', 'CASH FLOW'),
(NEWID(), 'A17', 2, 'A2016', 'Confirmation From DEPED Verifier'),
(NEWID(), 'A17', 2, 'A2008', 'Proof of Income, Payslip, COE, Remittance Receipt');
");

            migrationBuilder.Sql(@"
INSERT INTO LoanProductDocumentRequirements (Id, ProductCode, LoanType, Code, Description) VALUES
(NEWID(), 'C21', 1, 'A1004', 'Duly Accomplished Application Form'),
(NEWID(), 'C21', 1, 'CCR38', 'CASH FLOW'),
(NEWID(), 'C21', 1, 'A2008', 'Proof of Income, Payslip, COE, Remittance Receipt'),
(NEWID(), 'C21', 1, 'PIC03', 'CLIENT SELFIE WITH ID'),
(NEWID(), 'C21', 1, 'A2020', 'CIBI Report'),
(NEWID(), 'C21', 1, 'A2023', 'Bank Statement'),
(NEWID(), 'C21', 1, 'A2027', 'Others Required Docs (Optional)'),
(NEWID(), 'C21', 2, 'A1004', 'Duly Accomplished Application Form'),
(NEWID(), 'C21', 2, 'CCR38', 'CASH FLOW'),
(NEWID(), 'C21', 2, 'A2008', 'Proof of Income, Payslip, COE, Remittance Receipt'),
(NEWID(), 'C21', 2, 'A2027', 'Others Required Docs (Optional)');
");

            migrationBuilder.Sql(@"
INSERT INTO LoanProductDocumentRequirements (Id, ProductCode, LoanType, Code, Description) VALUES
(NEWID(), 'C02', 1, 'A1004', 'Duly Accomplished Application Form'),
(NEWID(), 'C02', 1, 'CCR38', 'CASH FLOW'),
(NEWID(), 'C02', 1, 'A2008', 'Proof of Income, Payslip, COE, Remittance Receipt'),
(NEWID(), 'C23', 1, 'A1004', 'Duly Accomplished Application Form'),
(NEWID(), 'C23', 1, 'CCR38', 'CASH FLOW'),
(NEWID(), 'C23', 1, 'A2008', 'Proof of Income, Payslip, COE, Remittance Receipt'),
(NEWID(), 'C35', 1, 'A1004', 'Duly Accomplished Application Form'),
(NEWID(), 'C35', 1, 'CCR38', 'CASH FLOW'),
(NEWID(), 'C35', 1, 'A2008', 'Proof of Income, Payslip, COE, Remittance Receipt');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LoanProductDocumentRequirements_ProductCode_LoanType_Code",
                table: "LoanProductDocumentRequirements");

            migrationBuilder.DropColumn(
                name: "LoanType",
                table: "LoanProductDocumentRequirements");

            migrationBuilder.CreateIndex(
                name: "IX_LoanProductDocumentRequirements_ProductCode_Code",
                table: "LoanProductDocumentRequirements",
                columns: new[] { "ProductCode", "Code" },
                unique: true);
        }
    }
}

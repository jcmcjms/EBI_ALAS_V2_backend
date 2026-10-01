using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace EBI.ALAS.Api.Migrations
{
    public partial class BackfillLoanTypeForReloans : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE LoanApplications
                SET LoanType = 'Renewal'
                WHERE CreationTypeCode = 1 AND LoanType = 'New';
            ");
            migrationBuilder.Sql(@"
                UPDATE LoanApplications
                SET RequiredApprovalTier = NULL
                WHERE CreationTypeCode = 1
                  AND LoanType = 'Renewal'
                  AND RequiredApprovalTier >= 3
                  AND Status IN ('ForApproval', 'ForChecking');
            ");
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE LoanApplications
                SET LoanType = 'New'
                WHERE CreationTypeCode = 1 AND LoanType = 'Renewal';
            ");
        }
    }
}

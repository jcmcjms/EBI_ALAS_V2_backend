using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class BackfillLoanTypeForReloans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill LoanType for existing reloans that were incorrectly stored as "New".
            // CreationTypeCode 1 = "Reloan" which should route as "Renewal".
            migrationBuilder.Sql(@"
                UPDATE LoanApplications
                SET LoanType = 'Renewal'
                WHERE CreationTypeCode = 1 AND LoanType = 'New';
            ");

            // Clear stale RequiredApprovalTier so reloans get re-routed correctly
            // on their next workflow transition.
            migrationBuilder.Sql(@"
                UPDATE LoanApplications
                SET RequiredApprovalTier = NULL
                WHERE CreationTypeCode = 1
                  AND LoanType = 'Renewal'
                  AND RequiredApprovalTier >= 3
                  AND Status IN ('ForApproval', 'ForChecking');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse: set LoanType back to "New" for reloans.
            migrationBuilder.Sql(@"
                UPDATE LoanApplications
                SET LoanType = 'New'
                WHERE CreationTypeCode = 1 AND LoanType = 'Renewal';
            ");
        }
    }
}

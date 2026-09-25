using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRoutingEscalationColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MatchedButUnstaffedTier",
                table: "LoanApplications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NoAuthorityReason",
                table: "LoanApplications",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MatchedButUnstaffedTier",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "NoAuthorityReason",
                table: "LoanApplications");
        }
    }
}

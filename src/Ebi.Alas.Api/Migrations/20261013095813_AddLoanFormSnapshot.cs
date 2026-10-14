using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebi.Alas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanFormSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FormJson",
                table: "LoanApplications",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FormJson",
                table: "LoanApplications");
        }
    }
}

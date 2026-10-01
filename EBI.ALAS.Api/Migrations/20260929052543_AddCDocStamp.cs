using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace EBI.ALAS.Api.Migrations
{
    public partial class AddCDocStamp : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CDocStamp",
                table: "LoanApplications",
                type: "decimal(18,2)",
                nullable: true);
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CDocStamp",
                table: "LoanApplications");
        }
    }
}

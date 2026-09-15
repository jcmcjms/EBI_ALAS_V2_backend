using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanProductChecklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoanProductChecklist",
                columns: table => new
                {
                    LoanProduct = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IdCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoanProductChecklist", x => new { x.LoanProduct, x.IdCode });
                    table.ForeignKey(
                        name: "FK_LoanProductChecklist_LoanProducts_LoanProduct",
                        column: x => x.LoanProduct,
                        principalTable: "LoanProducts",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoanProductChecklist");
        }
    }
}

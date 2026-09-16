using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserBranchCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Branches_Code",
                table: "Branches",
                column: "Code");

            migrationBuilder.CreateTable(
                name: "UserBranchCoverages",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    BranchCode = table.Column<string>(type: "nvarchar(20)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBranchCoverages", x => new { x.UserId, x.BranchCode });
                    table.ForeignKey(
                        name: "FK_UserBranchCoverages_Branches_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "Branches",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserBranchCoverages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserBranchCoverages_BranchCode",
                table: "UserBranchCoverages",
                column: "BranchCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserBranchCoverages");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Branches_Code",
                table: "Branches");
        }
    }
}

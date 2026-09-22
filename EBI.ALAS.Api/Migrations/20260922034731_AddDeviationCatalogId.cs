using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviationCatalogId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_DeviationCatalog",
                table: "DeviationCatalog");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "DeviationCatalog",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DeviationCatalog",
                table: "DeviationCatalog",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_DeviationCatalog_Description",
                table: "DeviationCatalog",
                column: "Description",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_DeviationCatalog",
                table: "DeviationCatalog");

            migrationBuilder.DropIndex(
                name: "IX_DeviationCatalog_Description",
                table: "DeviationCatalog");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "DeviationCatalog");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DeviationCatalog",
                table: "DeviationCatalog",
                column: "Description");
        }
    }
}

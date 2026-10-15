using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebi.Alas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserESignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ESignature",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ESignature",
                table: "Users");
        }
    }
}

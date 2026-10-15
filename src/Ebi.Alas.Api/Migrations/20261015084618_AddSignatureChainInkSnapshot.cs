using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebi.Alas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureChainInkSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignatureImage",
                table: "SignatureChainEntries",
                type: "nvarchar(max)",
                nullable: true);

            // Best-effort historical ink: current profile signature on slots
            // signed before this snapshot column existed.
            migrationBuilder.Sql("""
                UPDATE s
                SET s.SignatureImage = u.ESignature
                FROM SignatureChainEntries AS s
                INNER JOIN Users AS u ON u.Id = s.UserId
                WHERE s.Status = 2 AND s.SignatureImage IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignatureImage",
                table: "SignatureChainEntries");
        }
    }
}

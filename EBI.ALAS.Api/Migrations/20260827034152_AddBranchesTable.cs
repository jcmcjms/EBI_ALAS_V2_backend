using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Branches_Code",
                table: "Branches",
                column: "Code",
                unique: true);

            // Seed branch data
            var baseDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            migrationBuilder.InsertData(
                table: "Branches",
                columns: new[] { "Id", "Code", "Name", "IsActive", "CreatedAt" },
                values: new object[,]
                {
                    { 1, "000", "Lianga Branch", true, baseDate },
                    { 2, "002", "Barobo Branch", true, baseDate },
                    { 3, "003", "San Francisco Branch", true, baseDate },
                    { 4, "004", "Arasasan Branch", true, baseDate },
                    { 5, "005", "Hinatuan Branch", true, baseDate },
                    { 6, "006", "Tagum Branch", true, baseDate },
                    { 7, "007", "Tandag Branch", true, baseDate },
                    { 8, "008", "Butuan Branch", true, baseDate },
                    { 9, "009", "Bislig Branch", true, baseDate },
                    { 10, "011", "Head Office Branch", true, baseDate },
                    { 11, "012", "Cagayan Branch", true, baseDate },
                    { 12, "013", "Talisay Branch", true, baseDate },
                    { 13, "014", "General Santos Branch", true, baseDate },
                    { 14, "015", "Panabo Branch", true, baseDate },
                    { 15, "016", "Valencia Branch", true, baseDate },
                    { 16, "017", "Cateel Branch", true, baseDate },
                    { 17, "018", "Davao-Buhangin Branch", true, baseDate },
                    { 18, "019", "Tacloban Branch", true, baseDate },
                    { 19, "020", "Bacolod Branch", true, baseDate },
                    { 20, "021", "Iloilo Branch", true, baseDate },
                    { 21, "022", "Davao-Matina Branch", true, baseDate },
                    { 22, "023", "Trento Branch", true, baseDate },
                    { 23, "024", "Mati Branch", true, baseDate },
                    { 24, "025", "Bayugan Branch", true, baseDate },
                    { 25, "026", "Nabunturan Branch", true, baseDate },
                    { 26, "027", "Madrid Branch", true, baseDate },
                    { 27, "028", "Surigao Branch", true, baseDate },
                    { 28, "029", "Gingoog Branch", true, baseDate },
                    { 29, "030", "CTS (Mandaue) Branch", true, baseDate },
                    { 30, "031", "Ronda Branch", true, baseDate },
                    { 31, "991", "Corporate Center", true, baseDate },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Branches");
        }
    }
}

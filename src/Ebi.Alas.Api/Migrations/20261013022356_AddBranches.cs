using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebi.Alas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBranches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AreaCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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

            var seedTimestamp = new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);

            migrationBuilder.InsertData(
                table: "Branches",
                columns: new[] { "Id", "Code", "Name", "AreaCode", "Status", "CreatedAt", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000001"), "000", "Lianga Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000002"), "002", "Barobo Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000003"), "003", "San Francisco Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000004"), "004", "Arasasan Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000005"), "005", "Hinatuan Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000006"), "006", "Tagum Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000007"), "007", "Tandag Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000008"), "008", "Butuan Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000009"), "009", "Bislig Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000000a"), "011", "Head Office Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000000b"), "012", "Cagayan Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000000c"), "013", "Talisay Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000000d"), "014", "General Santos Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000000e"), "015", "Panabo Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000000f"), "016", "Valencia Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000010"), "017", "Cateel Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000011"), "018", "Davao-Buhangin Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000012"), "019", "Tacloban Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000013"), "020", "Bacolod Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000014"), "021", "Iloilo Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000015"), "022", "Davao-Matina Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000016"), "023", "Trento Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000017"), "024", "Mati Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000018"), "025", "Bayugan Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000019"), "026", "Nabunturan Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000001a"), "027", "Madrid Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000001b"), "028", "Surigao Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000001c"), "029", "Gingoog Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000001d"), "030", "CTS (Mandaue) Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000001e"), "031", "Ronda Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000001f"), "991", "Corporate Center", "NA", 0, seedTimestamp, seedTimestamp }
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

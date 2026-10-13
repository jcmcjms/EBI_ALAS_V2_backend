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

            // Area codes follow the prior system (DbInitializer.SeedBranchAreaCodesAsync).
            // 018 and 991 had no area assignment there and remain "NA".
            migrationBuilder.InsertData(
                table: "Branches",
                columns: new[] { "Id", "Code", "Name", "AreaCode", "Status", "CreatedAt", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000001"), "000", "Lianga Branch", "A2", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000002"), "002", "Barobo Branch", "A2", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000003"), "003", "San Francisco Branch", "A1", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000004"), "004", "Arasasan Branch", "A2", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000005"), "005", "Hinatuan Branch", "A2", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000006"), "006", "Tagum Branch", "A3", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000007"), "007", "Tandag Branch", "A2", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000008"), "008", "Butuan Branch", "A1", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000009"), "009", "Bislig Branch", "A2", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000000a"), "011", "Head Office Branch", "A3", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000000b"), "012", "Cagayan Branch", "A1", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000000c"), "013", "Talisay Branch", "A4", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000000d"), "014", "General Santos Branch", "A3", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000000e"), "015", "Panabo Branch", "A3", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000000f"), "016", "Valencia Branch", "A3", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000010"), "017", "Cateel Branch", "A2", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000011"), "018", "Davao-Buhangin Branch", "NA", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000012"), "019", "Tacloban Branch", "A4", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000013"), "020", "Bacolod Branch", "A4", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000014"), "021", "Iloilo Branch", "A4", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000015"), "022", "Davao-Matina Branch", "A3", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000016"), "023", "Trento Branch", "A1", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000017"), "024", "Mati Branch", "A3", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000018"), "025", "Bayugan Branch", "A1", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-000000000019"), "026", "Nabunturan Branch", "A1", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000001a"), "027", "Madrid Branch", "A2", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000001b"), "028", "Surigao Branch", "A1", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000001c"), "029", "Gingoog Branch", "A1", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000001d"), "030", "CTS (Mandaue) Branch", "A4", 0, seedTimestamp, seedTimestamp },
                    { new Guid("00000000-0000-0000-0000-00000000001e"), "031", "Ronda Branch", "A4", 0, seedTimestamp, seedTimestamp },
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

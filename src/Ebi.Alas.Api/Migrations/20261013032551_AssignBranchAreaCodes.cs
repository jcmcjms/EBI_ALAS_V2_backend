using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebi.Alas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AssignBranchAreaCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Area assignments from the prior system (DbInitializer.SeedBranchAreaCodesAsync).
            // 018 (Davao-Buhangin) and 991 (Corporate Center) were never assigned an area there.
            migrationBuilder.Sql("UPDATE Branches SET AreaCode = 'A1' WHERE Code IN ('003','008','012','023','025','026','028','029')");
            migrationBuilder.Sql("UPDATE Branches SET AreaCode = 'A2' WHERE Code IN ('000','002','004','005','007','009','017','027')");
            migrationBuilder.Sql("UPDATE Branches SET AreaCode = 'A3' WHERE Code IN ('006','011','014','015','016','022','024')");
            migrationBuilder.Sql("UPDATE Branches SET AreaCode = 'A4' WHERE Code IN ('013','019','020','021','030','031')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Branches SET AreaCode = 'NA'");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace EBI.ALAS.Api.Migrations
{
    public partial class AddTempPasswordExpiry : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TempPasswordExpiresAt",
                table: "Users",
                type: "datetime2",
                nullable: true);
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TempPasswordExpiresAt",
                table: "Users");
        }
    }
}

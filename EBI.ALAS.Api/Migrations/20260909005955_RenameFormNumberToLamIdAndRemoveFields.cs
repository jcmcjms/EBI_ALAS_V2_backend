using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class RenameFormNumberToLamIdAndRemoveFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoMaker",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "DateOfFirstRelease",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "ModeOfPayment",
                table: "LoanApplications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoMaker",
                table: "LoanApplications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateOfFirstRelease",
                table: "LoanApplications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModeOfPayment",
                table: "LoanApplications",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }
    }
}

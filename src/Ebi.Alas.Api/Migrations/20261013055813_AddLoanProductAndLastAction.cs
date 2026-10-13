using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebi.Alas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanProductAndLastAction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LastActionById",
                table: "LoanApplications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastActionDate",
                table: "LoanApplications",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "LastActionVerb",
                table: "LoanApplications",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Product",
                table: "LoanApplications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProductCode",
                table: "LoanApplications",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastActionById",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "LastActionDate",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "LastActionVerb",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "Product",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "ProductCode",
                table: "LoanApplications");
        }
    }
}

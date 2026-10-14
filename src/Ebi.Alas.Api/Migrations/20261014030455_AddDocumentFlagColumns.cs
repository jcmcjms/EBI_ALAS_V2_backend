using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebi.Alas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentFlagColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DocumentFlagMissingCount",
                table: "LoanApplications",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DocumentFlagReason",
                table: "LoanApplications",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DocumentsFlaggedAt",
                table: "LoanApplications",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DocumentsFlaggedById",
                table: "LoanApplications",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentFlagMissingCount",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "DocumentFlagReason",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "DocumentsFlaggedAt",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "DocumentsFlaggedById",
                table: "LoanApplications");
        }
    }
}

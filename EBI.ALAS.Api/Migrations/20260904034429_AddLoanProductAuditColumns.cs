using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanProductAuditColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UpdatedById",
                table: "LoanProducts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedDate",
                table: "LoanProducts",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_LoanProducts_UpdatedById",
                table: "LoanProducts",
                column: "UpdatedById");

            migrationBuilder.AddForeignKey(
                name: "FK_LoanProducts_Users_UpdatedById",
                table: "LoanProducts",
                column: "UpdatedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LoanProducts_Users_UpdatedById",
                table: "LoanProducts");

            migrationBuilder.DropIndex(
                name: "IX_LoanProducts_UpdatedById",
                table: "LoanProducts");

            migrationBuilder.DropColumn(
                name: "UpdatedById",
                table: "LoanProducts");

            migrationBuilder.DropColumn(
                name: "UpdatedDate",
                table: "LoanProducts");
        }
    }
}

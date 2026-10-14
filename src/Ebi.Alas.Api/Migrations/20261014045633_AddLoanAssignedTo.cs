using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebi.Alas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanAssignedTo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "LoanApplications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_AssignedToUserId",
                table: "LoanApplications",
                column: "AssignedToUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_AssignedToUserId",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "LoanApplications");
        }
    }
}

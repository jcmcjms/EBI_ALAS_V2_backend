using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebi.Alas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentRemarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentRemarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoanApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChecklistIdCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DocId = table.Column<int>(type: "int", nullable: true),
                    ParentRemarkId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AuthorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRemarks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRemarks_LoanApplicationId",
                table: "DocumentRemarks",
                column: "LoanApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRemarks_ParentRemarkId",
                table: "DocumentRemarks",
                column: "ParentRemarkId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentRemarks");
        }
    }
}

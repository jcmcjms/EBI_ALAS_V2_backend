using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoanApplicationId = table.Column<int>(type: "int", nullable: false),
                    ChecklistIdCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DocId = table.Column<int>(type: "int", nullable: true),
                    ParentRemarkId = table.Column<int>(type: "int", nullable: true),
                    AuthorId = table.Column<int>(type: "int", nullable: false),
                    AuthorRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRemarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentRemarks_DocumentRemarks_ParentRemarkId",
                        column: x => x.ParentRemarkId,
                        principalTable: "DocumentRemarks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentRemarks_LoanApplications_LoanApplicationId",
                        column: x => x.LoanApplicationId,
                        principalTable: "LoanApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentRemarks_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRemarks_AuthorId",
                table: "DocumentRemarks",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRemarks_LoanApplicationId_ChecklistIdCode_CreatedAt",
                table: "DocumentRemarks",
                columns: new[] { "LoanApplicationId", "ChecklistIdCode", "CreatedAt" });

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

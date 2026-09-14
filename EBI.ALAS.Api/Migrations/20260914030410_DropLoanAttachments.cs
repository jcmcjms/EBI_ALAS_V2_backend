using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class DropLoanAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guard: refuse to drop while rows exist so a mis-ordered deploy
            // can never silently destroy uploaded files. Archive first if this
            // ever throws.
            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'dbo.LoanAttachments', N'U') IS NOT NULL
                BEGIN
                    IF EXISTS (SELECT 1 FROM dbo.LoanAttachments)
                        THROW 50001, 'LoanAttachments still contains rows - archive them before dropping the table.', 1;

                    DROP TABLE dbo.LoanAttachments;
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoanAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoanApplicationId = table.Column<int>(type: "int", nullable: false),
                    UploadedById = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoanAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoanAttachments_LoanApplications_LoanApplicationId",
                        column: x => x.LoanApplicationId,
                        principalTable: "LoanApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoanAttachments_Users_UploadedById",
                        column: x => x.UploadedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoanAttachments_LoanApplicationId",
                table: "LoanAttachments",
                column: "LoanApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanAttachments_UploadedById",
                table: "LoanAttachments",
                column: "UploadedById");
        }
    }
}

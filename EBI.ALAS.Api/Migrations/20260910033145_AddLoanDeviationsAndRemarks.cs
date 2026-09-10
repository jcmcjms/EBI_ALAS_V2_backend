using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBI.ALAS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanDeviationsAndRemarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoanAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoanApplicationId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UploadedById = table.Column<int>(type: "int", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "LoanDeviations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoanApplicationId = table.Column<int>(type: "int", nullable: false),
                    ReasonText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EncoderJustification = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsFeeOverride = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoanDeviations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoanDeviations_LoanApplications_LoanApplicationId",
                        column: x => x.LoanApplicationId,
                        principalTable: "LoanApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviationRemarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoanDeviationId = table.Column<int>(type: "int", nullable: false),
                    ParentRemarkId = table.Column<int>(type: "int", nullable: true),
                    AuthorId = table.Column<int>(type: "int", nullable: false),
                    AuthorRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviationRemarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviationRemarks_DeviationRemarks_ParentRemarkId",
                        column: x => x.ParentRemarkId,
                        principalTable: "DeviationRemarks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviationRemarks_LoanDeviations_LoanDeviationId",
                        column: x => x.LoanDeviationId,
                        principalTable: "LoanDeviations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeviationRemarks_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviationRemarks_AuthorId",
                table: "DeviationRemarks",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviationRemarks_LoanDeviationId_CreatedAt",
                table: "DeviationRemarks",
                columns: new[] { "LoanDeviationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviationRemarks_ParentRemarkId",
                table: "DeviationRemarks",
                column: "ParentRemarkId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanAttachments_LoanApplicationId",
                table: "LoanAttachments",
                column: "LoanApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanAttachments_UploadedById",
                table: "LoanAttachments",
                column: "UploadedById");

            migrationBuilder.CreateIndex(
                name: "IX_LoanDeviations_LoanApplicationId_SortOrder",
                table: "LoanDeviations",
                columns: new[] { "LoanApplicationId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviationRemarks");

            migrationBuilder.DropTable(
                name: "LoanAttachments");

            migrationBuilder.DropTable(
                name: "LoanDeviations");
        }
    }
}

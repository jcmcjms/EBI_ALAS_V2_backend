using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebi.Alas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalAuthorities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalAuthorities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    AllowNew = table.Column<bool>(type: "bit", nullable: false),
                    AllowRenewal = table.Column<bool>(type: "bit", nullable: false),
                    MaxSeverity = table.Column<int>(type: "int", nullable: false),
                    MaxTotalExposure = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ScopeType = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalAuthorities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalAuthorityUsers",
                columns: table => new
                {
                    ApprovalAuthorityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalAuthorityUsers", x => new { x.ApprovalAuthorityId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ApprovalAuthorityUsers_ApprovalAuthorities_ApprovalAuthorityId",
                        column: x => x.ApprovalAuthorityId,
                        principalTable: "ApprovalAuthorities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalAuthorityUsers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalAuthorities_Key",
                table: "ApprovalAuthorities",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalAuthorityUsers_UserId",
                table: "ApprovalAuthorityUsers",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalAuthorityUsers");

            migrationBuilder.DropTable(
                name: "ApprovalAuthorities");
        }
    }
}

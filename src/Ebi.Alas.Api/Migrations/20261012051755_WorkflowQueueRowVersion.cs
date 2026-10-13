using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebi.Alas.Api.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowQueueRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "WorkflowQueueItems",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MiddleName",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // Split FullName into structured name parts before dropping it.
            // 1 token → FirstName only (LastName gets the same token so the
            // required LastName invariant holds). 2 tokens → First/Last.
            // 3+ tokens → First / middle run / Last.
            migrationBuilder.Sql("""
                UPDATE Users
                SET
                    FirstName = CASE
                        WHEN NULLIF(LTRIM(RTRIM(FullName)), '') IS NULL THEN N''
                        WHEN CHARINDEX(N' ', LTRIM(RTRIM(FullName))) = 0 THEN LTRIM(RTRIM(FullName))
                        ELSE LEFT(LTRIM(RTRIM(FullName)), CHARINDEX(N' ', LTRIM(RTRIM(FullName))) - 1)
                    END,
                    LastName = CASE
                        WHEN NULLIF(LTRIM(RTRIM(FullName)), '') IS NULL THEN N''
                        WHEN CHARINDEX(N' ', LTRIM(RTRIM(FullName))) = 0 THEN LTRIM(RTRIM(FullName))
                        ELSE REVERSE(LEFT(REVERSE(LTRIM(RTRIM(FullName))), CHARINDEX(N' ', REVERSE(LTRIM(RTRIM(FullName)))) - 1))
                    END,
                    MiddleName = CASE
                        WHEN NULLIF(LTRIM(RTRIM(FullName)), '') IS NULL THEN N''
                        WHEN CHARINDEX(N' ', LTRIM(RTRIM(FullName))) = 0 THEN N''
                        ELSE LTRIM(RTRIM(SUBSTRING(
                            LTRIM(RTRIM(FullName)),
                            CHARINDEX(N' ', LTRIM(RTRIM(FullName))) + 1,
                            LEN(LTRIM(RTRIM(FullName))) - CHARINDEX(N' ', LTRIM(RTRIM(FullName))) - CHARINDEX(N' ', REVERSE(LTRIM(RTRIM(FullName))))
                        )))
                    END
                WHERE NULLIF(LTRIM(RTRIM(FullName)), '') IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "FullName",
                table: "Users");

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SystemSettings_Users_UpdatedById",
                        column: x => x.UpdatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_UpdatedById",
                table: "SystemSettings",
                column: "UpdatedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "WorkflowQueueItems");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MiddleName",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "Users",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }
    }
}

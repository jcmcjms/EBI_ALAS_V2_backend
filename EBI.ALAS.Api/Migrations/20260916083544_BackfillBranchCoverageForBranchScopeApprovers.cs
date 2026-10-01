using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace EBI.ALAS.Api.Migrations
{
    public partial class BackfillBranchCoverageForBranchScopeApprovers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO UserBranchCoverages (UserId, BranchCode)
                SELECT u.Id, u.BranchId
                FROM Users u
                JOIN ApprovalAuthorities a ON a.[Key] = u.ApprovalAuthorityKey
                WHERE a.ScopeType = 0                       -- Branch
                  AND u.IsActive = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM UserBranchCoverages c
                      WHERE c.UserId = u.Id
                  );
            ");
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE c
                FROM UserBranchCoverages c
                INNER JOIN Users u ON u.Id = c.UserId
                INNER JOIN ApprovalAuthorities a ON a.[Key] = u.ApprovalAuthorityKey
                WHERE a.ScopeType = 0                       -- Branch
                  AND c.BranchCode = u.BranchId;            -- home branch only
            ");
        }
    }
}

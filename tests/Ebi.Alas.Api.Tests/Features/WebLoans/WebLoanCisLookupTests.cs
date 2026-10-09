using Ebi.Alas.Api.Features.WebLoans;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Ebi.Alas.Api.Infrastructure.WebLoans;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.WebLoans;

public sealed class WebLoanCisLookupTests
{
    private static async Task<(SqliteConnection Connection, WebLoanDbContext Db)> CreateDbAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WebLoanDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new WebLoanDbContext(options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE cis_info (
                cis_no TEXT PRIMARY KEY,
                fname TEXT,
                mname TEXT,
                lname TEXT,
                title TEXT,
                appelation TEXT,
                p_bday TEXT,
                h_sadd TEXT,
                h_barangay TEXT,
                h_village TEXT,
                h_city TEXT,
                h_state_prov TEXT,
                h_zip TEXT,
                occupation TEXT,
                b_jtitle TEXT,
                b_region_code TEXT,
                b_division_code TEXT,
                b_station_code TEXT,
                b_employee_no TEXT,
                bk TEXT,
                bch TEXT
            );
            CREATE TABLE loan_acct_info (
                bk TEXT NOT NULL,
                bch TEXT NOT NULL,
                acct_no TEXT NOT NULL,
                name TEXT,
                cis_no TEXT,
                credit_limit TEXT,
                used_credit TEXT,
                borrower_type TEXT,
                cat_mis_group2 TEXT,
                solicitor TEXT,
                PRIMARY KEY (bk, bch, acct_no)
            );
            CREATE TABLE cis_info_misc_data (
                cis_no TEXT NOT NULL,
                id_code INTEGER NOT NULL,
                value_str TEXT,
                PRIMARY KEY (cis_no, id_code)
            );
            CREATE TABLE check_list_data (
                cis_no TEXT NOT NULL,
                check_list_item TEXT NOT NULL,
                description TEXT,
                submitted TEXT,
                expiration TEXT,
                PRIMARY KEY (cis_no, check_list_item)
            );
            CREATE TABLE mis_group (
                frp_id INTEGER PRIMARY KEY,
                group_no INTEGER,
                id_code TEXT,
                description TEXT,
                path TEXT
            );
            CREATE TABLE loan_purpose (
                path TEXT PRIMARY KEY,
                description TEXT
            );
            """);
        return (connection, db);
    }

    [Fact]
    public async Task GetCisDetailAsync_MapsBorrowerAndAccountsFromWebLoanTables()
    {
        var (connection, db) = await CreateDbAsync();
        await using (connection)
        await using (db)
        {
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO cis_info (cis_no, fname, mname, lname, h_sadd, h_city, occupation, b_employee_no)
                VALUES ('0880914721', 'JUAN', 'DELA', 'CRUZ', '123 Rizal St', 'Butuan', 'Teacher', 'E-1');
                INSERT INTO loan_acct_info (bk, bch, acct_no, name, cis_no, credit_limit, used_credit, borrower_type, cat_mis_group2, solicitor)
                VALUES ('001', '011', '05-13081-1', 'JUAN CRUZ', '0880914721', 50000, 1500, 'Principal', 'MIS-PATH', 'SOL-PATH');
                INSERT INTO cis_info_misc_data (cis_no, id_code, value_str) VALUES ('0880914721', 14, 'AG-1');
                INSERT INTO check_list_data (cis_no, check_list_item, description, expiration)
                VALUES ('0880914721', 'CCR10', '2010-01-01', NULL);
                INSERT INTO check_list_data (cis_no, check_list_item, description, expiration)
                VALUES ('0880914721', 'CCR07', 'NTHP-OK', '2027-01-01');
                INSERT INTO mis_group (frp_id, group_no, id_code, description, path)
                VALUES (1, 1, 'AG-1', 'Government Agency', NULL),
                       (2, 1, 'M', 'MIS Unit', 'MIS-PATH'),
                       (3, 2, 'S', 'Requesting Officer', 'SOL-PATH');
                """);

            var detail = await new WebLoanReader(db).GetCisDetailAsync("0880914721", CancellationToken.None);

            Assert.NotNull(detail);
            Assert.Equal("JUAN", detail!.Borrower.FirstName);
            Assert.Equal("DELA", detail.Borrower.MiddleName);
            Assert.Equal("CRUZ", detail.Borrower.LastName);
            Assert.Equal("123 Rizal St, Butuan", detail.Borrower.Address);
            Assert.Equal("E-1", detail.Borrower.EmployeeNumber);
            Assert.Equal("Government Agency", detail.Borrower.AgencyType);
            Assert.Equal("MIS Unit", detail.Borrower.MisAgency);
            Assert.Equal("Requesting Officer", detail.Borrower.RequestingOfficer);
            Assert.NotNull(detail.Borrower.LengthOfService);
            Assert.Contains("years", detail.Borrower.LengthOfService);

            var account = Assert.Single(detail.Accounts);
            Assert.Equal("011-05-13081-1", account.AccountId);
            Assert.Equal(1500m, account.UsedCredit);
            Assert.Equal(50000m, account.CreditLimit);
        }
    }

    [Fact]
    public async Task GetCisDetailAsync_Missing_ReturnsNull()
    {
        var (connection, db) = await CreateDbAsync();
        await using (connection)
        await using (db)
        {
            var detail = await new WebLoanReader(db).GetCisDetailAsync("nope", CancellationToken.None);
            Assert.Null(detail);
        }
    }

    [Fact]
    public void WebLoanAccountId_FormatsAndParses()
    {
        Assert.Equal("011-05-1", WebLoanAccountId.Format("011", "05-1"));
        var (branch, account) = WebLoanAccountId.Parse("011-05-13081-1");
        Assert.Equal("011", branch);
        Assert.Equal("05-13081-1", account);
    }

    [Fact]
    public void PendingLoanDto_AcceptsByteCreationType()
    {
        byte creationType = 1;
        var dto = new PendingLoanDto(
            "LN-1",
            1000m,
            0.05m,
            30,
            6,
            "C16 - AFOS",
            null,
            creationType,
            "Reloan",
            10m);
        Assert.Equal((byte?)1, dto.CreationType);
    }

    [Fact]
    public void OutstandingLoanDto_IncludesStatusAndAmort()
    {
        var dto = new OutstandingLoanDto(
            "LN-1", 1000m, 800m, 250m,
            "2024-01-01", "2025-01-01",
            "A13", "A13 - Current", "A13 - AFOS");
        Assert.Equal("A13 - Current", dto.ProductStatus);
        Assert.Equal(250m, dto.AmortAmount);
    }
}

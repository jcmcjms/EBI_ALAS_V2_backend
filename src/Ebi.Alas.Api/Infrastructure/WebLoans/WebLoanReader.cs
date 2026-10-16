using System.Data;
using System.Globalization;
using Ebi.Alas.Api.Features.WebLoans;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Infrastructure.WebLoans;

public sealed class WebLoanReader(AlasDbContext db, IOptions<WebLoanOptions> webLoanOptions) : IWebLoanReader
{
    private const int AgencyTypeCode = 14;
    private const string LengthOfServiceItem = "CCR10";
    private const string NthpItem = "CCR07";

    private readonly string _prefix = webLoanOptions.Value.NamePrefix
        ?? throw new InvalidOperationException("WebLoan:NamePrefix is required for WebLoanReader.");

    private string T(string tableName) => WebLoanSql.Qualify(_prefix, tableName);

    public async Task<IReadOnlyList<WebLoanChecklistRow>> GetChecklistItemsAsync(
        string cisNo,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cisNo);
        FormattableString sql = $@"
            SELECT check_list_item, description, submitted
            FROM {T("check_list_data")}
            WHERE cis_no = {cisNo}
            ORDER BY check_list_item";
        var rows = await db.Database
            .SqlQuery<CheckListSqlRow>(sql)
            .ToListAsync(cancellationToken);
        return rows
            .Select(r => new WebLoanChecklistRow(r.check_list_item ?? string.Empty, r.description, r.submitted))
            .ToList();
    }

    public async Task<IReadOnlyList<WebLoanChecklistCatalogRow>> GetChecklistCatalogAsync(
        IReadOnlyCollection<string> idCodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idCodes);
        if (idCodes.Count == 0)
        {
            return [];
        }

        var codes = idCodes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (codes.Count == 0)
        {
            return [];
        }

        var rows = await QueryInAsync<CheckListCatalogSqlRow>(
            $"id_code, description",
            T("check_list_all"),
            "id_code",
            codes,
            cancellationToken);
        return rows
            .Select(r => new WebLoanChecklistCatalogRow(r.id_code ?? string.Empty, r.description))
            .ToList();
    }

    public async Task<IReadOnlyList<CisSearchResult>> SearchCisAsync(
        string keyword,
        int max,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        var limit = Math.Clamp(max, 1, 50);
        var term = keyword.Trim();
        FormattableString sql = $@"
            SELECT TOP ({limit}) cis_no, fname, mname, lname
            FROM {T("cis_info")}
            WHERE cis_no LIKE {"%" + term + "%"}
               OR fname LIKE {"%" + term + "%"}
               OR lname LIKE {"%" + term + "%"}
            ORDER BY lname";
        var rows = await db.Database
            .SqlQuery<CisSearchSqlRow>(sql)
            .ToListAsync(cancellationToken);
        return rows
            .Select(r => new CisSearchResult(
                r.cis_no ?? string.Empty,
                ((r.fname ?? string.Empty) + " " + (r.mname ?? string.Empty) + " " + (r.lname ?? string.Empty)).Trim(),
                null))
            .ToList();
    }

    public async Task<CisDetail?> GetCisDetailAsync(string cisNo, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cisNo);
        var term = cisNo.Trim();

        FormattableString cisSql = $@"
            SELECT TOP (1) cis_no, fname, mname, lname, title, appelation, p_bday,
                   h_sadd, h_barangay, h_village, h_city, h_state_prov, h_zip,
                   occupation, b_jtitle, b_region_code, b_division_code, b_station_code, b_employee_no, bk, bch
            FROM {T("cis_info")}
            WHERE cis_no = {term}";
        var cis = await db.Database.SqlQuery<CisInfoSqlRow>(cisSql).FirstOrDefaultAsync(cancellationToken);
        if (cis is null)
        {
            return null;
        }

        FormattableString acctSql = $@"
            SELECT TOP (50) bk, bch, acct_no, name, credit_limit, used_credit, borrower_type, cat_mis_group2, solicitor
            FROM {T("loan_acct_info")}
            WHERE cis_no = {term}
            ORDER BY bch, acct_no";
        var accounts = await db.Database.SqlQuery<LoanAccountSqlRow>(acctSql).ToListAsync(cancellationToken);

        FormattableString miscSql = $@"
            SELECT TOP (1) value_str
            FROM {T("cis_info_misc_data")}
            WHERE cis_no = {term} AND id_code = {AgencyTypeCode}";
        var agencyMisc = await db.Database.SqlQuery<CisMiscSqlRow>(miscSql).FirstOrDefaultAsync(cancellationToken);

        FormattableString hireSql = $@"
            SELECT TOP (1) description
            FROM {T("check_list_data")}
            WHERE cis_no = {term} AND check_list_item = {LengthOfServiceItem}";
        var hireDateRow = await db.Database.SqlQuery<CheckListDescSqlRow>(hireSql).FirstOrDefaultAsync(cancellationToken);

        var misPaths = accounts
            .Select(a => a.cat_mis_group2)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var solicitorPaths = accounts
            .Select(a => a.solicitor)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? agencyDesc = null;
        if (agencyMisc?.value_str is { Length: > 0 } agencyCode)
        {
            FormattableString agencySql = $@"
                SELECT TOP (1) description
                FROM {T("mis_group")}
                WHERE id_code = {agencyCode}";
            agencyDesc = (await db.Database.SqlQuery<CheckListDescSqlRow>(agencySql)
                .FirstOrDefaultAsync(cancellationToken))?.description;
        }

        string? misAgencyDesc = null;
        if (misPaths.Count > 0)
        {
            var row = await QueryInAsync<CheckListDescSqlRow>(
                "description",
                T("mis_group"),
                "path",
                misPaths,
                cancellationToken,
                top: 1);
            misAgencyDesc = row.FirstOrDefault()?.description;
        }

        string? solicitorDesc = null;
        if (solicitorPaths.Count > 0)
        {
            var row = await QueryInAsync<CheckListDescSqlRow>(
                "description",
                T("mis_group"),
                "path",
                solicitorPaths,
                cancellationToken,
                top: 1,
                extraWhere: "group_no = 2");
            solicitorDesc = row.FirstOrDefault()?.description;
        }

        return new CisDetail(
            new CisBorrower(
                cis.cis_no ?? term,
                cis.fname ?? string.Empty,
                cis.mname,
                cis.lname ?? string.Empty,
                cis.title,
                cis.appelation,
                ParseBirthDate(cis.p_bday),
                BuildAddress(cis),
                AgencyType: agencyDesc,
                PositionTitle: cis.occupation ?? cis.b_jtitle,
                Region: null,
                RegionCode: cis.b_region_code,
                DivisionCode: cis.b_division_code,
                StationCode: cis.b_station_code,
                EmployeeNumber: cis.b_employee_no,
                MisAgency: misAgencyDesc,
                RequestingOfficer: solicitorDesc,
                LengthOfService: ComputeLengthOfService(hireDateRow?.description)),
            accounts
                .Select(a => new CisAccount(
                    a.bk ?? string.Empty,
                    a.bch ?? string.Empty,
                    a.acct_no ?? string.Empty,
                    (a.bch ?? string.Empty) + "-" + (a.acct_no ?? string.Empty),
                    a.name,
                    a.credit_limit,
                    a.used_credit,
                    a.borrower_type))
                .ToList());
    }

    public async Task<IReadOnlyList<LoanProductLookup>> GetActiveProductsAsync(
        int max,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(max, 1, 500);
        FormattableString sql = $@"
            SELECT TOP ({limit}) id_code, description
            FROM {T("loan_product")}
            WHERE expiration IS NULL
            ORDER BY description";
        var rows = await db.Database.SqlQuery<ProductSqlRow>(sql).ToListAsync(cancellationToken);
        return rows
            .Select(r => new LoanProductLookup(
                r.id_code ?? string.Empty,
                r.description ?? string.Empty,
                IsRetired: false,
                InterestRatePerMonth: 0m))
            .ToList();
    }

    public async Task<IReadOnlyList<LoanProductLookup>> GetAllProductsAsync(
        int max,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(max, 1, 500);
        FormattableString sql = $@"
            SELECT TOP ({limit}) id_code, description, expiration
            FROM {T("loan_product")}
            ORDER BY description";
        var rows = await db.Database.SqlQuery<ProductWithExpirationSqlRow>(sql).ToListAsync(cancellationToken);
        return rows
            .Select(r => new LoanProductLookup(
                r.id_code ?? string.Empty,
                r.description ?? string.Empty,
                IsRetired: r.expiration != null,
                InterestRatePerMonth: 0m))
            .ToList();
    }

    public async Task<IReadOnlyList<OutstandingLoanRow>> GetOutstandingAsync(
        string cifNo,
        int max,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cifNo);
        var limit = Math.Clamp(max, 1, 50);
        FormattableString sql = $@"
            SELECT TOP ({limit}) acct_no, used_credit
            FROM {T("loan_acct_info")}
            WHERE cis_no = {cifNo} AND used_credit > 0
            ORDER BY acct_no";
        var rows = await db.Database.SqlQuery<OutstandingRowSqlRow>(sql).ToListAsync(cancellationToken);
        return rows
            .Select(r => new OutstandingLoanRow(
                r.acct_no ?? string.Empty,
                "Active",
                r.used_credit ?? 0))
            .ToList();
    }

    public async Task<OutstandingLoansResponse?> GetAccountOutstandingLoansAsync(
        string cisNo,
        string accountId,
        int pageSize,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cisNo);
        var (branchCode, accountNo) = WebLoanAccountId.Parse(accountId);
        var belongs = await AccountBelongsAsync(cisNo, branchCode, accountNo, cancellationToken);
        if (!belongs)
        {
            return null;
        }

        pageSize = Math.Clamp(pageSize, 1, 200);
        pageNumber = Math.Max(1, pageNumber);
        var skip = (pageNumber - 1) * pageSize;

        FormattableString sql = $@"
            SELECT ld.loan_no, ld.principal, ld.principal_bal,
                   CASE WHEN ld.loan_product IN ('C35','C23') THEN ld.principal
                        ELSE ad.total_amort END AS amort_amount,
                   ld.date_granted, ld.date_maturity, ld.loan_product,
                   ld.loan_status, lp.description AS product_desc
            FROM {T("loan_data")} AS ld
            LEFT JOIN {T("amort_data")} AS ad
                ON ld.loan_no = ad.loan_no AND ld.acct_no = ad.acct_no
               AND ld.bch = ad.bch AND ad.amort_no = 1
            LEFT JOIN {T("loan_product")} AS lp ON ld.loan_product = lp.id_code
            WHERE ld.bch = {branchCode} AND ld.acct_no = {accountNo}
              AND {_prefix}.is_loan(ld.loan_no) = 1
              AND ld.loan_status != 10
              AND ld.principal_bal > 0
            ORDER BY ld.date_granted DESC
            OFFSET {skip} ROWS FETCH NEXT {pageSize} ROWS ONLY";

        var rows = await db.Database
            .SqlQuery<OutstandingLoanSqlRow>(sql)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var loans = rows.Select(r =>
        {
            var statusLabel = ResolveLoanStatusLabel(r.loan_status);
            var productCode = r.loan_product ?? string.Empty;
            return new OutstandingLoanDto(
                r.loan_no,
                r.principal,
                r.principal_bal,
                r.amort_amount,
                r.date_granted?.ToString("yyyy-MM-dd"),
                r.date_maturity?.ToString("yyyy-MM-dd"),
                productCode,
                string.IsNullOrWhiteSpace(productCode)
                    ? statusLabel
                    : $"{productCode} - {statusLabel}",
                string.IsNullOrWhiteSpace(r.product_desc)
                    ? productCode
                    : $"{productCode} - {r.product_desc}");
        }).ToList();

        return new OutstandingLoansResponse(cisNo, accountId, branchCode, accountNo, loans);
    }

    public async Task<PendingLoanResponse?> GetAccountPendingLoanAsync(
        string cisNo,
        string accountId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cisNo);
        var (branchCode, accountNo) = WebLoanAccountId.Parse(accountId);
        var belongs = await AccountBelongsAsync(cisNo, branchCode, accountNo, cancellationToken);
        if (!belongs)
        {
            return null;
        }

        FormattableString sql = $@"
            SELECT pld.loan_no, ld.principal, ld.granted_rate, ld.total_amortization,
                   ld.date_granted, ld.date_maturity, ld.creation_type, ld.c_doc_stamp,
                   ld.loan_product, ld.cat_loan_purpose, lp.description AS product_desc,
                   lp2.description AS loan_purpose
            FROM {T("pre_loan_data")} AS pld
            LEFT JOIN {T("loan_data")} AS ld
                ON pld.loan_no = ld.loan_no AND pld.acct_no = ld.acct_no AND pld.bch = ld.bch
            LEFT JOIN {T("loan_product")} AS lp ON ld.loan_product = lp.id_code
            LEFT JOIN {T("loan_purpose")} AS lp2 ON ld.cat_loan_purpose = lp2.path
            WHERE pld.bch = {branchCode} AND pld.acct_no = {accountNo}
              AND pld.approved_date IS NULL AND pld.prepared_date IS NULL
              AND pld.released_date IS NULL AND pld.void_date IS NULL
            ORDER BY pld.loan_no";

        var rows = await db.Database
            .SqlQuery<PendingLoanSqlRow>(sql)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        FormattableString nthpSql = $@"
            SELECT TOP (1) description, expiration, submitted
            FROM {T("check_list_data")}
            WHERE cis_no = {cisNo} AND check_list_item = {NthpItem}";
        var nthp = await db.Database.SqlQuery<NthpSqlRow>(nthpSql).FirstOrDefaultAsync(cancellationToken);

        var loans = rows.Select(r =>
        {
            int? termDays = null;
            if (r.date_granted is { } granted && r.date_maturity is { } maturity)
            {
                termDays = Math.Max(0, (maturity.Date - granted.Date).Days);
            }

            return new PendingLoanDto(
                r.loan_no ?? string.Empty,
                r.principal,
                r.granted_rate,
                termDays,
                r.total_amortization,
                string.IsNullOrWhiteSpace(r.product_desc)
                    ? (r.loan_product ?? string.Empty)
                    : $"{r.loan_product} - {r.product_desc}",
                string.IsNullOrWhiteSpace(r.loan_purpose) ? r.cat_loan_purpose : r.loan_purpose,
                r.creation_type,
                CreationTypeLabel(r.creation_type),
                r.c_doc_stamp);
        }).ToList();

        return new PendingLoanResponse(
            cisNo, accountId, branchCode, accountNo, loans,
            Nthp: nthp?.description,
            NthpDate: (nthp?.expiration ?? nthp?.submitted)?.ToString("yyyy-MM-dd"));
    }

    public async Task<WebLoanLoanDataRow?> GetLoanByNoAsync(
        string loanNo,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loanNo);
        var normalized = loanNo.Trim();
        FormattableString sql = $@"
            SELECT TOP (1) loan_no, loan_product
            FROM {T("loan_data")}
            WHERE loan_no = {normalized}";
        var row = await db.Database
            .SqlQuery<LoanByNoSqlRow>(sql)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null || string.IsNullOrWhiteSpace(row.loan_no))
        {
            return null;
        }

        return new WebLoanLoanDataRow(row.loan_no, row.loan_product ?? string.Empty);
    }

    public async Task<string?> GetCatLoanClassAsync(
        string branchCode,
        string loanNo,
        string productCode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(loanNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT TOP (1) cat_loan_class
            FROM {T("loan_data")}
            WHERE bch = @branchCode AND loan_no = @loanNo AND loan_product = @productCode";

        var pBranch = command.CreateParameter();
        pBranch.ParameterName = "@branchCode";
        pBranch.Value = branchCode;
        command.Parameters.Add(pBranch);

        var pLoanNo = command.CreateParameter();
        pLoanNo.ParameterName = "@loanNo";
        pLoanNo.Value = loanNo;
        command.Parameters.Add(pLoanNo);

        var pProduct = command.CreateParameter();
        pProduct.ParameterName = "@productCode";
        pProduct.Value = productCode;
        command.Parameters.Add(pProduct);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is string value && value.Length > 0 ? value : null;
    }

    private async Task<bool> AccountBelongsAsync(
        string cisNo,
        string branchCode,
        string accountNo,
        CancellationToken cancellationToken)
    {
        FormattableString sql = $@"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM {T("loan_acct_info")}
                WHERE cis_no = {cisNo} AND bch = {branchCode} AND acct_no = {accountNo}
            ) THEN CAST(1 AS int) ELSE CAST(0 AS int) END";
        return await db.Database.SqlQuery<int>(sql).SingleAsync(cancellationToken) == 1;
    }

    private async Task<List<T>> QueryInAsync<T>(
        string selectList,
        string tableName,
        string column,
        IReadOnlyList<string> values,
        CancellationToken cancellationToken,
        int? top = null,
        string? extraWhere = null)
        where T : class
    {
        if (values.Count == 0)
        {
            return [];
        }

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var parameters = new (string Name, string Value)[values.Count];
        var names = new string[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            parameters[i] = ($"@p{i}", values[i]);
            names[i] = parameters[i].Name;
        }

        var topClause = top is null ? string.Empty : $"TOP ({top.Value}) ";
        var extra = string.IsNullOrWhiteSpace(extraWhere) ? string.Empty : $" AND {extraWhere}";
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {topClause}{selectList} FROM {tableName} WHERE {column} IN ({string.Join(", ", names)}){extra}";

        foreach (var (name, value) in parameters)
        {
            var p = command.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            command.Parameters.Add(p);
        }

        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(MapRow<T>(reader));
        }

        return results;
    }

    private static T MapRow<T>(IDataRecord reader) where T : class
    {
        var row = Activator.CreateInstance<T>();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var prop = typeof(T).GetProperty(reader.GetName(i));
            if (prop is null || reader.IsDBNull(i))
            {
                continue;
            }

            prop.SetValue(row, Convert.ChangeType(reader.GetValue(i), prop.PropertyType));
        }

        return row;
    }

    private static string ResolveLoanStatusLabel(byte? code) => code switch
    {
        0 => "Current",
        1 => "Pastdue Performing",
        2 => "Pastdue Non-Performing",
        3 => "Litigation / ITL",
        4 => "Transfer of Asset",
        5 => "Write-off",
        _ => "Unknown",
    };

    private static string CreationTypeLabel(byte? code) => code switch
    {
        0 => "New Loan",
        1 => "Reloan",
        2 => "Restructured",
        6 => "Additional Loan",
        _ => "Unknown",
    };

    internal static string? ComputeLengthOfService(string? rawHireDate)
    {
        if (string.IsNullOrWhiteSpace(rawHireDate))
        {
            return null;
        }

        if (!DateTime.TryParse(rawHireDate.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var hire))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var totalMonths = Math.Max(0, ((now.Year - hire.Year) * 12) + (now.Month - hire.Month));
        return $"{totalMonths / 12} years, {totalMonths % 12} months";
    }

    internal static string? ParseBirthDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string[] formats = ["yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy", "yyyy/MM/dd", "dd-MM-yyyy"];
        if (DateTime.TryParseExact(raw.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt.ToString("yyyy-MM-dd");
        }

        return DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback)
            ? fallback.ToString("yyyy-MM-dd")
            : null;
    }

    private static string? BuildAddress(CisInfoSqlRow cis)
    {
        string?[] parts = [cis.h_zip, cis.h_sadd, cis.h_city, cis.h_state_prov, cis.h_barangay, cis.h_village];
        var joined = string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        return joined.Length == 0 ? null : joined;
    }

    private sealed class CheckListSqlRow
    {
        public string? check_list_item { get; set; }

        public string? description { get; set; }

        public DateTime? submitted { get; set; }
    }

    private sealed class CheckListCatalogSqlRow
    {
        public string? id_code { get; set; }

        public string? description { get; set; }
    }

    private sealed class CheckListDescSqlRow
    {
        public string? description { get; set; }
    }

    private sealed class CisSearchSqlRow
    {
        public string? cis_no { get; set; }

        public string? fname { get; set; }

        public string? mname { get; set; }

        public string? lname { get; set; }
    }

    private sealed class CisMiscSqlRow
    {
        public string? value_str { get; set; }
    }

    private sealed class ProductSqlRow
    {
        public string? id_code { get; set; }

        public string? description { get; set; }
    }

    private sealed class ProductWithExpirationSqlRow
    {
        public string? id_code { get; set; }

        public string? description { get; set; }

        public DateTime? expiration { get; set; }
    }

    private sealed class OutstandingRowSqlRow
    {
        public string? acct_no { get; set; }

        public decimal? used_credit { get; set; }
    }

    private sealed class OutstandingLoanSqlRow
    {
        public string? loan_no { get; set; }

        public decimal? principal { get; set; }

        public decimal? principal_bal { get; set; }

        public decimal? amort_amount { get; set; }

        public DateTime? date_granted { get; set; }

        public DateTime? date_maturity { get; set; }

        public string? loan_product { get; set; }

        public byte? loan_status { get; set; }

        public string? product_desc { get; set; }
    }

    private sealed class PendingLoanSqlRow
    {
        public string? loan_no { get; set; }

        public decimal? principal { get; set; }

        public decimal? granted_rate { get; set; }

        public int? total_amortization { get; set; }

        public DateTime? date_granted { get; set; }

        public DateTime? date_maturity { get; set; }

        public byte? creation_type { get; set; }

        public decimal? c_doc_stamp { get; set; }

        public string? loan_product { get; set; }

        public string? cat_loan_purpose { get; set; }

        public string? product_desc { get; set; }

        public string? loan_purpose { get; set; }
    }

    private sealed class NthpSqlRow
    {
        public string? description { get; set; }

        public DateTime? expiration { get; set; }

        public DateTime? submitted { get; set; }
    }

    private sealed class LoanByNoSqlRow
    {
        public string? loan_no { get; set; }

        public string? loan_product { get; set; }
    }

    private sealed class LoanAccountSqlRow
    {
        public string? bk { get; set; }

        public string? bch { get; set; }

        public string? acct_no { get; set; }

        public string? name { get; set; }

        public decimal? credit_limit { get; set; }

        public decimal? used_credit { get; set; }

        public string? borrower_type { get; set; }

        public string? cat_mis_group2 { get; set; }

        public string? solicitor { get; set; }
    }

    private sealed class CisInfoSqlRow
    {
        public string? cis_no { get; set; }

        public string? fname { get; set; }

        public string? mname { get; set; }

        public string? lname { get; set; }

        public string? title { get; set; }

        public string? appelation { get; set; }

        public string? p_bday { get; set; }

        public string? h_sadd { get; set; }

        public string? h_barangay { get; set; }

        public string? h_village { get; set; }

        public string? h_city { get; set; }

        public string? h_state_prov { get; set; }

        public string? h_zip { get; set; }

        public string? occupation { get; set; }

        public string? b_jtitle { get; set; }

        public string? b_region_code { get; set; }

        public string? b_division_code { get; set; }

        public string? b_station_code { get; set; }

        public string? b_employee_no { get; set; }

        public string? bk { get; set; }

        public string? bch { get; set; }
    }
}

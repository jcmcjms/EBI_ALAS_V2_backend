using System.Globalization;
using Ebi.Alas.Api.Features.WebLoans;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Infrastructure.WebLoans;

public sealed class WebLoanReader(WebLoanDbContext db) : IWebLoanReader
{
    public async Task<IReadOnlyList<CisSearchResult>> SearchCisAsync(
        string keyword,
        int max,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        var limit = Math.Clamp(max, 1, 50);
        var term = keyword.Trim();
        return await db.CisInfos.AsNoTracking()
            .Where(c => c.CisNo.Contains(term)
                || (c.FirstName != null && c.FirstName.Contains(term))
                || (c.LastName != null && c.LastName.Contains(term)))
            .OrderBy(c => c.LastName)
            .Take(limit)
            .Select(c => new CisSearchResult(
                c.CisNo,
                ((c.FirstName ?? string.Empty) + " " + (c.MiddleName ?? string.Empty) + " " + (c.LastName ?? string.Empty)).Trim(),
                null))
            .ToListAsync(cancellationToken);
    }

    public async Task<CisDetail?> GetCisDetailAsync(string cisNo, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cisNo);
        var term = cisNo.Trim();
        var cis = await db.CisInfos.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CisNo == term, cancellationToken);
        if (cis is null)
        {
            return null;
        }

        var accounts = await db.LoanAccounts.AsNoTracking()
            .Where(a => a.CisNo == term)
            .OrderBy(a => a.BranchCode)
            .ThenBy(a => a.AccountNo)
            .Take(50)
            .Select(a => new
            {
                a.BankCode,
                a.BranchCode,
                a.AccountNo,
                a.Name,
                a.CreditLimit,
                a.UsedCredit,
                a.BorrowerType,
                a.MisGroup2,
                a.Solicitor,
            })
            .ToListAsync(cancellationToken);

        var agencyMisc = await db.CisMiscDatas.AsNoTracking()
            .FirstOrDefaultAsync(m => m.CisNo == term && m.IdCode == AgencyTypeCode, cancellationToken);
        var hireDateRow = await db.CheckLists.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CisNo == term && c.CheckListItem == LengthOfServiceItem, cancellationToken);

        var misPaths = accounts
            .Select(a => a.MisGroup2)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct()
            .ToList();
        var solicitorPaths = accounts
            .Select(a => a.Solicitor)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct()
            .ToList();

        var agencyGroup = agencyMisc?.ValueStr is { Length: > 0 } agencyCode
            ? await db.MisGroups.AsNoTracking()
                .FirstOrDefaultAsync(m => m.IdCode == agencyCode, cancellationToken)
            : null;

        var misAgencyDesc = misPaths.Count == 0
            ? null
            : await db.MisGroups.AsNoTracking()
                .Where(m => misPaths.Contains(m.Path ?? string.Empty))
                .Select(m => m.Description)
                .FirstOrDefaultAsync(cancellationToken);

        var solicitorDesc = solicitorPaths.Count == 0
            ? null
            : await db.MisGroups.AsNoTracking()
                .Where(m => solicitorPaths.Contains(m.Path ?? string.Empty) && m.GroupNo == 2)
                .Select(m => m.Description)
                .FirstOrDefaultAsync(cancellationToken);

        return new CisDetail(
            new CisBorrower(
                cis.CisNo,
                cis.FirstName ?? string.Empty,
                cis.MiddleName,
                cis.LastName ?? string.Empty,
                cis.Title,
                cis.Appelation,
                ParseBirthDate(cis.BirthDateRaw),
                BuildAddress(cis),
                AgencyType: agencyGroup?.Description,
                PositionTitle: cis.Occupation ?? cis.JobTitle,
                Region: null,
                RegionCode: cis.RegionCode,
                DivisionCode: cis.DivisionCode,
                StationCode: cis.StationCode,
                EmployeeNumber: cis.EmployeeNo,
                MisAgency: misAgencyDesc,
                RequestingOfficer: solicitorDesc,
                LengthOfService: ComputeLengthOfService(hireDateRow?.Description)),
            accounts
                .Select(a => new CisAccount(
                    a.BankCode ?? string.Empty,
                    a.BranchCode ?? string.Empty,
                    a.AccountNo ?? string.Empty,
                    (a.BranchCode ?? string.Empty) + "-" + (a.AccountNo ?? string.Empty),
                    a.Name,
                    a.CreditLimit,
                    a.UsedCredit,
                    a.BorrowerType))
                .ToList());
    }

    private const int AgencyTypeCode = 14;
    private const string LengthOfServiceItem = "CCR10";
    private const string NthpItem = "CCR07";

    private static string? ComputeLengthOfService(string? rawHireDate)
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

    public async Task<IReadOnlyList<LoanProductLookup>> GetActiveProductsAsync(
        int max,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(max, 1, 500);
        return await db.LoanProducts.AsNoTracking()
            .Where(p => p.Expiration == null)
            .OrderBy(p => p.ProductName)
            .Take(limit)
            .Select(p => new LoanProductLookup(
                p.ProductCode,
                p.ProductName,
                IsRetired: false,
                InterestRatePerMonth: 0m))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LoanProductLookup>> GetAllProductsAsync(
        int max,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(max, 1, 500);
        return await db.LoanProducts.AsNoTracking()
            .OrderBy(p => p.ProductName)
            .Take(limit)
            .Select(p => new LoanProductLookup(
                p.ProductCode,
                p.ProductName,
                IsRetired: p.Expiration != null,
                InterestRatePerMonth: 0m))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OutstandingLoanRow>> GetOutstandingAsync(
        string cifNo,
        int max,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cifNo);
        var limit = Math.Clamp(max, 1, 50);
        return await db.LoanAccounts.AsNoTracking()
            .Where(a => a.CisNo == cifNo && a.UsedCredit > 0)
            .OrderBy(a => a.AccountNo)
            .Take(limit)
            .Select(a => new OutstandingLoanRow(
                a.AccountNo ?? string.Empty,
                a.UsedCredit > 0 ? "Active" : "Closed",
                a.UsedCredit ?? 0))
            .ToListAsync(cancellationToken);
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
        var belongs = await db.LoanAccounts.AsNoTracking()
            .AnyAsync(a => a.CisNo == cisNo && a.BranchCode == branchCode && a.AccountNo == accountNo,
                cancellationToken);
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
            FROM dbo.loan_data AS ld
            LEFT JOIN dbo.amort_data AS ad
                ON ld.loan_no = ad.loan_no AND ld.acct_no = ad.acct_no
               AND ld.bch = ad.bch AND ad.amort_no = 1
            LEFT JOIN dbo.loan_product AS lp ON ld.loan_product = lp.id_code
            WHERE ld.bch = {branchCode} AND ld.acct_no = {accountNo}
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
        var belongs = await db.LoanAccounts.AsNoTracking()
            .AnyAsync(a => a.CisNo == cisNo && a.BranchCode == branchCode && a.AccountNo == accountNo,
                cancellationToken);
        if (!belongs)
        {
            return null;
        }

        FormattableString sql = $@"
            SELECT pld.loan_no, ld.principal, ld.granted_rate, ld.total_amortization,
                   ld.date_granted, ld.date_maturity, ld.creation_type, ld.c_doc_stamp,
                   ld.loan_product, ld.cat_loan_purpose, lp.description AS product_desc,
                   lp2.description AS loan_purpose
            FROM dbo.pre_loan_data AS pld
            LEFT JOIN dbo.loan_data AS ld
                ON pld.loan_no = ld.loan_no AND pld.acct_no = ld.acct_no AND pld.bch = ld.bch
            LEFT JOIN dbo.loan_product AS lp ON ld.loan_product = lp.id_code
            LEFT JOIN dbo.loan_purpose AS lp2 ON ld.cat_loan_purpose = lp2.path
            WHERE pld.bch = {branchCode} AND pld.acct_no = {accountNo}
              AND pld.approved_date IS NULL AND pld.prepared_date IS NULL
              AND pld.released_date IS NULL AND pld.void_date IS NULL
            ORDER BY pld.loan_no";

        var rows = await db.Database
            .SqlQuery<PendingLoanSqlRow>(sql)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var nthp = await db.CheckLists.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CisNo == cisNo && c.CheckListItem == NthpItem, cancellationToken);

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
            Nthp: nthp?.Description,
            NthpDate: (nthp?.Expiration ?? nthp?.Submitted)?.ToString("yyyy-MM-dd"));
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

    private static string? BuildAddress(WebLoanCisInfo cis)
    {
        string?[] parts = [cis.Zip, cis.HouseStreet, cis.City, cis.StateProvince, cis.Barangay, cis.Village];
        var joined = string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        return joined.Length == 0 ? null : joined;
    }

    private static string? ParseBirthDate(string? raw)
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
}

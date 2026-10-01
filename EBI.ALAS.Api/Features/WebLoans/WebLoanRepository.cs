using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace EBI.ALAS.Api.Features.WebLoans;
public class WebLoanRepository(IDbContextFactory<WebLoanDbContext> contextFactory) : IWebLoanRepository
{
    public async Task<CisInfo?> GetCisInfoAsync(string cisNo, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.CisInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CisNo == cisNo, ct);
    }
    public async Task<CisInfoMiscData?> GetAgencyTypeAsync(string cisNo, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.CisInfoMiscDatas
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.CisNo == cisNo && m.IdCode == CisInfoMiscData.AgencyTypeIdCode,
                ct);
    }
    public async Task<CheckListData?> GetLengthOfServiceAsync(string cisNo, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.CheckListDatas
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.CisNo == cisNo && c.CheckListItem == CheckListData.LengthOfServiceItem,
                ct);
    }
    public async Task<MisGroup?> GetMisGroupByIdCodeAsync(string idCode, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.MisGroups
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.IdCode == idCode, ct);
    }
    public async Task<IReadOnlyList<MisGroup>> GetMisGroupsByPathsAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct = default)
    {
        if (paths.Count == 0) return Array.Empty<MisGroup>();
        var distinct = paths.Distinct().ToList();
        var placeholders = string.Join(", ",
            Enumerable.Range(0, distinct.Count).Select(i => $"@p{i}"));
        var sql = $@"
            SELECT frp_id, group_no, pid_code, id_code, grp_level,
                   description, description2, path
            FROM webloan.dbo.mis_group
            WHERE path IN ({placeholders})";
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.MisGroups
            .FromSqlRaw(sql, distinct.Cast<object>().ToArray())
            .AsNoTracking()
            .ToListAsync(ct);
    }
    public async Task<IReadOnlyList<MisGroup>> GetSolicitorsByPathsAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct = default)
    {
        if (paths.Count == 0) return Array.Empty<MisGroup>();
        var distinct = paths.Distinct().ToList();
        var placeholders = string.Join(", ",
            Enumerable.Range(0, distinct.Count).Select(i => $"@p{i}"));
        var sql = $@"
            SELECT frp_id, group_no, pid_code, id_code, grp_level,
                   description, description2, path
            FROM webloan.dbo.mis_group
            WHERE path IN ({placeholders})
              AND group_no = 2";
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.MisGroups
            .FromSqlRaw(sql, distinct.Cast<object>().ToArray())
            .AsNoTracking()
            .ToListAsync(ct);
    }
    public async Task<IReadOnlyList<LoanAcctInfo>> GetAccountsByCisAsync(
        string cisNo,
        string? branchCode = null,
        CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var query = context.LoanAcctInfos
            .AsNoTracking()
            .Where(a => a.CisNo == cisNo);
        if (!string.IsNullOrEmpty(branchCode))
        {
            query = query.Where(a => a.BranchCode == branchCode);
        }
        return await query
            .OrderBy(a => a.AccountNo)
            .ToListAsync(ct);
    }
    public async Task<bool> AccountBelongsToCisAsync(
        string cisNo,
        string branchCode,
        string accountNo,
        CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.LoanAcctInfos
            .AsNoTracking()
            .AnyAsync(
                a => a.CisNo == cisNo
                  && a.BranchCode == branchCode
                  && a.AccountNo == accountNo,
                ct);
    }
    public async Task<IReadOnlyList<OutstandingLoanRow>> GetOutstandingLoansAsync(
        string branchCode,
        string accountNo,
        int pageSize = 50,
        int pageNumber = 1,
        CancellationToken ct = default)
    {
        FormattableString sql = $@"
            SELECT
                ld.bk, ld.bch, ld.acct_no, ld.loan_no,
                ld.loan_product, ld.payment_interval, ld.total_amortization,
                ld.granted_rate, ld.effective_rate, ld.cat_loan_purpose,
                ld.principal, ld.applied_principal,
                ld.principal_bal, ld.amort_amount, ld.over_bal,
                ld.date_granted, ld.date_maturity, ld.loan_status,
                ld.close_date, ld.creation_type,
                CASE
                    WHEN ld.loan_product IN ('C35','C23') THEN ld.principal
                    ELSE ad.total_amort
                END AS computed_amort_amount,
                ISNULL(ld.loan_product, '') + ' - ' + ISNULL(lp.description, '') AS product_with_desc
            FROM webloan.dbo.loan_data AS ld
            LEFT JOIN webloan.dbo.amort_data AS ad
                ON  ld.loan_no = ad.loan_no
                AND ld.acct_no = ad.acct_no
                AND ld.bch     = ad.bch
                AND ad.amort_no = 1
            LEFT JOIN webloan.dbo.loan_product AS lp
                ON  ld.loan_product = lp.id_code
            WHERE ld.acct_no = {accountNo}
              AND ld.bch     = {branchCode}
              AND webloan.dbo.is_loan(ld.loan_no) = 1
              AND ld.loan_status != 10
              AND ld.principal_bal > 0
            ORDER BY ld.date_granted DESC
            OFFSET {(pageNumber - 1) * pageSize} ROWS
            FETCH NEXT {pageSize} ROWS ONLY";
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.OutstandingLoanRows
            .FromSqlInterpolated(sql)
            .AsNoTracking()
            .ToListAsync(ct);
    }
    public async Task<IReadOnlyList<PendingLoanRow>> GetPendingLoansAsync(
        string branchCode,
        string accountNo,
        CancellationToken ct = default)
    {
        FormattableString sql = $@"
            SELECT
                pld.bch,
                pld.acct_no,
                pld.loan_no,
                ld.principal,
                ld.granted_rate,
                ld.total_amortization,
                ld.date_granted,
                ld.date_maturity,
                ld.creation_type,
                ld.c_doc_stamp,
                CASE ld.creation_type
                    WHEN 0 THEN 'New Loan'
                    WHEN 1 THEN 'Reloan'
                    WHEN 2 THEN 'Restructured'
                    WHEN 6 THEN 'Additional Loan'
                    ELSE 'Unknown'
                END AS creation_type_label,
                DATEDIFF(DAY, ld.date_granted, ld.date_maturity) AS total_term_days,
                ISNULL(ld.loan_product, '') + ' - ' + ISNULL(lp.description, '') AS product_with_desc,
                lp2.description AS loan_purpose,
                cld.description AS nthp,
                cld.expiration AS nthp_date
            FROM webloan.dbo.pre_loan_data AS pld
            LEFT JOIN webloan.dbo.loan_data AS ld
                ON pld.loan_no = ld.loan_no
               AND pld.acct_no = ld.acct_no
               AND pld.bch     = ld.bch
            LEFT JOIN webloan.dbo.loan_product AS lp
                ON ld.loan_product = lp.id_code
            LEFT JOIN webloan.dbo.loan_purpose AS lp2
                ON ld.cat_loan_purpose = lp2.path
            LEFT JOIN webloan.dbo.loan_acct_info AS la
                ON pld.acct_no = la.acct_no
               AND pld.bch     = la.bch
            LEFT JOIN webloan.dbo.check_list_data AS cld
                ON la.cis_no        = cld.cis_no
               AND cld.check_list_item = 'CCR07'
            WHERE pld.approved_date IS NULL
              AND pld.prepared_date IS NULL
              AND pld.released_date IS NULL
              AND pld.void_date     IS NULL
              AND pld.bch           = {branchCode}
              AND pld.acct_no       = {accountNo}
            ORDER BY pld.bch, pld.acct_no, pld.loan_no";
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.PendingLoanRows
            .FromSqlInterpolated(sql)
            .AsNoTracking()
            .ToListAsync(ct);
    }
    public async Task<IReadOnlyList<LoanProductLookup>> GetActiveLoanProductsAsync(CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.LoanProducts
            .AsNoTracking()
            .Where(p => p.Expiration == null)
            .OrderBy(p => p.IdCode)
            .ToListAsync(ct);
    }
    public async Task<IReadOnlyList<LoanProductLookup>> GetAllLoanProductsAsync(CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.LoanProducts
            .AsNoTracking()
            .OrderBy(p => p.IdCode)
            .ToListAsync(ct);
    }
    public async Task<string?> GetCatLoanClassAsync(
        string branchCode,
        string loanNo,
        string productCode,
        CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                CASE
                    WHEN EXISTS (
                        SELECT 1 FROM webloan.dbo.loan_data
                        WHERE bch = @branchCode AND loan_no = @loanNo AND loan_product = @productCode
                    )
                    THEN ISNULL(
                        (SELECT TOP (1) cat_loan_class
                         FROM webloan.dbo.loan_data
                         WHERE bch = @branchCode AND loan_no = @loanNo AND loan_product = @productCode),
                        '__NULL__'
                    )
                    ELSE '__NOT_FOUND__'
                END AS result";
        var pBranch = command.CreateParameter();
        pBranch.ParameterName = "@branchCode";
        pBranch.Value = branchCode;
        pBranch.Size = 50;
        command.Parameters.Add(pBranch);
        var pLoanNo = command.CreateParameter();
        pLoanNo.ParameterName = "@loanNo";
        pLoanNo.Value = loanNo;
        pLoanNo.Size = 50;
        command.Parameters.Add(pLoanNo);
        var pProduct = command.CreateParameter();
        pProduct.ParameterName = "@productCode";
        pProduct.Value = productCode;
        pProduct.Size = 50;
        command.Parameters.Add(pProduct);
        var result = await command.ExecuteScalarAsync(ct);
        if (result is null || result == DBNull.Value) return null;
        var strResult = (string)result;
        if (strResult == "__NOT_FOUND__") return null;
        if (strResult == "__NULL__") return string.Empty;
        return strResult;
    }
    public async Task<IReadOnlyList<CheckListData>> GetCocreeItemsAsync(
        string cisNo,
        CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.CheckListDatas
            .AsNoTracking()
            .Where(c => c.CisNo == cisNo
                        && CheckListData.CocreeItems.Contains(c.CheckListItem))
            .ToListAsync(ct);
    }
    public async Task<PreLoanData?> GetPreLoanDataByLoanNoAsync(
        string loanNo,
        CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.PreLoanDatas
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.LoanNo == loanNo, ct);
    }
    public async Task<IReadOnlyDictionary<string, PreLoanData>> GetPreLoanDataByLoanNosAsync(
        IEnumerable<string> loanNos,
        CancellationToken ct = default)
    {
        var nos = loanNos.Distinct().ToList();
        if (nos.Count == 0) return new Dictionary<string, PreLoanData>();

        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.PreLoanDatas
            .AsNoTracking()
            .Where(p => nos.Contains(p.LoanNo))
            .ToDictionaryAsync(p => p.LoanNo, ct);
    }
}

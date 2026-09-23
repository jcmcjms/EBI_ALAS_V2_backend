using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.WebLoans;

// Each method opens its own DbContext via the factory because the
// service layer runs multiple lookups in parallel. EF Core's DbContext
// is not thread-safe — concurrent operations on the same instance
// throw "A second operation was started on this context instance…".
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
        // Single-row lookup. The mis_group table has an index on
        // (group_no, id_code) — a non-group-aware lookup scans that
        // index; cheap for a single value.
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

        // Distinct to keep the IN-clause list compact; account lists
        // commonly repeat the same cat_mis_group2 across multiple accts.
        var distinct = paths.Distinct().ToList();

        // SQL: WHERE path IN (@p0, @p1, ...) — the (group_no, path) index
        // covers path-only filters via index seek + residual filter on
        // group_no.
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

        // Same shape as GetMisGroupsByPathsAsync but with the explicit
        // group_no = 2 filter so we match the original sample SQL
        // (mg3.path = la.solicitor AND mg3.group_no = 2).
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

        // When branchCode is non-null (non-Admin callers), scope accounts
        // to the caller's branch so an encoder only sees their own
        // branch's accounts. Admin callers pass null and see all branches.
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
        // Composite-key check on (BranchCode, AccountNo) + CIS verification.
        // (bch, acct_no) is the natural key of dbo.loan_acct_info — the
        // cheapest correct ownership test. The CIS check is explicit so a
        // caller cannot probe (cisNo, accountNo) pairs across tenants by
        // guessing one half. (bch, acct_no) together are the unique
        // identifier; including bch in the predicate also stops a caller
        // from picking a (cisNo, accountNo) pair that exists under a
        // different branch.
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
        // Raw SQL is the right tool here:
        //   * `webloan.dbo.is_loan(loan_no)` is a T-SQL scalar UDF EF cannot
        //     translate. The server-side query optimizer evaluates the UDF
        //     inside the same execution plan; pulling all rows and filtering
        //     in C# would be strictly slower AND would defeat the existing
        //     index on (acct_no, bch) — IX_loan_data_acct_bch_status.
        //   * `loan_status != 10` is a row-level filter pushed to the same
        //     execution; same reasoning.
        //   * The CASE-computed `computed_amort_amount` projection needs a
        //     LEFT JOIN to dbo.amort_data (filtered on amort_no = 1) and
        //     CASE logic that EF cannot translate. The JOIN is on the
        //     natural key of amort_data — (bk, bch, acct_no, loan_no) —
        //     matching the original sample SQL.
        // Branch scoping note: previously this method accepted a nullable
        // `bch` to express the JWT-derived branch + Admin bypass
        // (`(@bch IS NULL OR bch = @bch)`). After the move to the
        // combined-`accountId` route, the branch is taken from the URL
        // and treated as part of the account identity — there is no
        // bypass and no JWT-derived branch. The (bch, acct_no) pair is
        // an exact match; the existing index covers it.
        // Why a dedicated projection row (OutstandingLoanRow) instead of
        // adding `ComputedAmortAmount` to the LoanData entity:
        //   * `ComputedAmortAmount` is a DERIVED column, not a webloan
        //     column. EF enforces that every mapped property on the entity
        //     be projected by the raw SQL; a `[NotMapped]` property would
        //     never be populated; a `[Column]` attribute would be a lie
        //     because the column doesn't exist in `dbo.loan_data`.
        //   * OutstandingLoanRow is a keyless entity that matches
        //     EXACTLY the columns this query projects (LoanData's 19 +
        //     ComputedAmortAmount). EF's materializer maps each column
        //     positionally to the property of the same name.
        // FromSqlInterpolated parameterizes both inputs as DbParameters —
        // no SQL injection. The FormattableString overload is the only
        // one that accepts inline values safely.
        // LEFT JOIN semantics: when amort_data has no row for the (bk,
        // bch, acct_no, loan_no, amort_no=1) tuple, the CASE falls through
        // to NULL for non-C35/C23 products — the UI renders this as "—".
        // principal_bal > 0 filter: drop rows with a settled balance of 0
        // (e.g. fully-paid but not yet status=10, or zero at issuance).
        // NULLs are intentionally retained — a missing balance is treated
        // as "unknown, show it" rather than "hide it", because `NULL != 0`
        // evaluates to NULL and a bare `principal_bal <> 0` predicate
        // would silently drop those rows too.
        // product_with_desc: a SECOND LEFT JOIN to webloan.dbo.loan_product
        // on (ld.loan_product = lp.id_code) enriches each row with a
        // human-readable description, producing
        //   ld.loan_product + ' - ' + lp.description
        // for the UI ("C35 - Quick Loan", etc.). The pending-loan
        // endpoint uses the same join shape (see
        // GetPendingLoansAsync) so the same product_with_desc string
        // surfaces from both endpoints without a separate
        // per-row repository lookup.
        // ISNULL(lp.description, ''): the LEFT JOIN can miss when an
        // open loan carries a product code that no longer exists in
        // loan_product (e.g. product was retired, loans still active).
        // In SQL Server, `NULL + ' - ' + NULL` is NULL, not
        // "<code> - ", so we coerce the description to '' and let the
        // service layer trim the trailing separator when projecting to
        // the DTO. The code half of the concat is non-nullable in
        // practice but is also ISNULL-wrapped for symmetry.
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

        // Materialize via the dedicated projection entity. AsNoTracking is
        // implied by the context's default (QueryTrackingBehavior.NoTracking
        // is set on the WebLoanDbContext); set explicitly for clarity.
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
        // Consolidated five-table LEFT JOIN against the original sample
        // SQL. (bch, acct_no) is an exact match from the URL's combined
        // `accountId` parameter; all four workflow dates NULL means
        // "in flight" (prepared, not yet approved/released/voided).
        // One execution returns everything the service needs to render
        // the pending-loan response:
        //   * pre_loan_data identifiers + workflow gate
        //   * loan_data scalars via (loan_no, acct_no, bch) join
        //     (principal, granted_rate, total_amortization, dates,
        //     creation_type)
        //   * loan_product description via (loan_product = id_code) join
        //     — assembled into "<code> - <description>" in SQL
        //   * loan_purpose description via (cat_loan_purpose = path)
        //     join
        //   * loan_acct_info.cis_no hop on (acct_no, bch) — the
        //     authoritative CIS for the (bch, acct_no) pair
        //   * check_list_data CCR07 row on (cis_no, item='CCR07') —
        //     NTHP amount + NTHP date
        // Replaces the previous N+1 fan-out (1 pre_loan_data + N
        // loan_data + N loan_product + N loan_purpose + 1 NTHP
        // round-trips). For an account with 3 in-flight rows, the old
        // shape issued 1 + 3 + 3 + 3 + 1 = 11 round-trips; this query
        // issues 1.
        // Derived expressions computed in SQL:
        //   * `creation_type_label` — the original CASE block
        //     (0=New Loan, 1=Reloan, 2=Restructured, 6=Additional Loan,
        //     ELSE 'Unknown'). Mirrored in WebLoanRegions.CreationTypeLabel
        //     for type-safe consumer code; the SQL label is used as the
        //     authoritative source here because it ships with the row.
        //   * `total_term_days` — DATEDIFF(DAY, date_granted,
        //     date_maturity). Replaces the legacy approximation of
        //     `total_amortization * 30`, which drifted by up to ±1 day
        //     per period. NULL when either date is NULL.
        //   * `product_with_desc` — ISNULL-wrapped concat to keep "<code> - "
        //     instead of NULL when the product row is missing.
        // Cartesian-product caveat (CCR07): the LEFT JOIN against
        // check_list_data is a true cartesian match — if there are
        // multiple CCR07 rows for the same cis_no (different vintages),
        // each pre_loan_data row is duplicated. The service layer
        // de-duplicates NTHP by reading from the first result row only
        // (per-loan fields are identical across duplicates). To
        // suppress duplicates at the SQL level, wrap the check_list_data
        // join in a subquery with TOP 1 ordered by expiration DESC.
        // Ordered deterministically by (bch, acct_no, loan_no) so
        // repeat calls return the same shape — the schema permits
        // duplicates for (bch, acct_no) and "FirstOrDefault" would
        // silently pick a different one each call.
        // All inputs are parameterized via FromSqlInterpolated → no SQL
        // injection. The `USE webloan;` preamble from the original
        // sample SQL is omitted: the connection string already targets
        // the webloan database, so the statement is redundant.
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

        // Materialize via the dedicated projection entity
        // (PendingLoanRow). AsNoTracking is implied by the context's
        // default (QueryTrackingBehavior.NoTracking is set on the
        // WebLoanDbContext); set explicitly for clarity.
        return await context.PendingLoanRows
            .FromSqlInterpolated(sql)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LoanProductLookup>> GetActiveLoanProductsAsync(CancellationToken ct = default)
    {
        // Filter on `expiration IS NULL` to surface only products that
        // are still active in webloan. The endpoint projects only id_code
        // + description, but we hydrate the full entity (no extra cost
        // beyond a single narrow row) so the service can decide what to
        // expose. Loan_product is a small lookup table (~tens of rows
        // in practice) so no pagination is needed — the whole set fits
        // in one trip.
        // Ordered by id_code ascending for a deterministic response; the
        // PK on id_code makes this an index scan with no sort cost.
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.LoanProducts
            .AsNoTracking()
            .Where(p => p.Expiration == null)
            .OrderBy(p => p.IdCode)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LoanProductLookup>> GetAllLoanProductsAsync(CancellationToken ct = default)
    {
        // Same shape as GetActiveLoanProductsAsync but without the
        // `expiration IS NULL` filter — the sync needs to see retired
        // rows too so it can mark the matching ALAS mirror row
        // IsRetired=true. The LoanProductLookup entity now exposes
        // Expiration (nullable DateTime) so the sync can read the
        // retirement signal in one round-trip; no second query needed.
        // Webloan's loan_product table is small enough that the full
        // scan is cheaper than a delta query — and a delta would
        // miss rows webloan DELETED entirely (rare, but possible if
        // the DBA cleans up). The full set keeps the sync robust.
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.LoanProducts
            .AsNoTracking()
            .OrderBy(p => p.IdCode)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Returns the cat_loan_class value for a loan, or null if no loan row exists.
    /// Uses a sentinel to distinguish "row exists but cat_loan_class IS NULL" from "no row found".
    /// </summary>
    public async Task<string?> GetCatLoanClassAsync(
        string branchCode,
        string loanNo,
        string productCode,
        CancellationToken ct = default)
    {
        // Raw ADO.NET via DbConnection — completely bypasses EF Core's
        // property mapper. This is essential because:
        //   1. The loan_data table in webloan may not have cat_loan_class
        //      as a real column (it is a computed/joined value in some schemas).
        //   2. EF Core's FromSql on a keyless entity wraps the SQL in a
        //      subquery that selects ALL mapped properties, causing
        //      "Invalid column name" when cat_loan_class isn't in the DB.
        //   3. SqlQuery<string?>() still goes through EF's result materializer.
        // Using raw ADO.NET means only the explicitly-named cat_loan_class
        // column is ever sent to or from SQL — no EF property mapping.
        // Composite input (bch, loan_no, loan_product) is caller-supplied.
        // All three are parameterized via DbParameter — no SQL injection.
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        // Use COUNT(*) to first check if the loan row exists, then fetch cat_loan_class.
        // This distinguishes "no row" from "row exists but cat_loan_class IS NULL".
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

        // ExecuteScalar returns:
        //   '__NOT_FOUND__' → no loan row exists → return null (caller maps to 404)
        //   '__NULL__' → loan exists but cat_loan_class IS NULL → return empty string
        //   non-null string → the actual cat_loan_class value
        if (result is null || result == DBNull.Value) return null;
        var strResult = (string)result;
        if (strResult == "__NOT_FOUND__") return null;
        if (strResult == "__NULL__") return string.Empty;
        return strResult;
    }
}
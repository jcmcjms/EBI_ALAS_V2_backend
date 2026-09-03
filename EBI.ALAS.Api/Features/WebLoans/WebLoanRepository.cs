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

    public async Task<IReadOnlyList<LoanAcctInfo>> GetAccountsByCisAsync(string cisNo, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.LoanAcctInfos
            .AsNoTracking()
            .Where(a => a.CisNo == cisNo)
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
        //
        // Branch scoping note: previously this method accepted a nullable
        // `bch` to express the JWT-derived branch + Admin bypass
        // (`(@bch IS NULL OR bch = @bch)`). After the move to the
        // combined-`accountId` route, the branch is taken from the URL
        // and treated as part of the account identity — there is no
        // bypass and no JWT-derived branch. The (bch, acct_no) pair is
        // an exact match; the existing index covers it.
        //
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
        //
        // FromSqlInterpolated parameterizes both inputs as DbParameters —
        // no SQL injection. The FormattableString overload is the only
        // one that accepts inline values safely.
        //
        // LEFT JOIN semantics: when amort_data has no row for the (bk,
        // bch, acct_no, loan_no, amort_no=1) tuple, the CASE falls through
        // to NULL for non-C35/C23 products — the UI renders this as "—".
        //
        // principal_bal > 0 filter: drop rows with a settled balance of 0
        // (e.g. fully-paid but not yet status=10, or zero at issuance).
        // NULLs are intentionally retained — a missing balance is treated
        // as "unknown, show it" rather than "hide it", because `NULL != 0`
        // evaluates to NULL and a bare `principal_bal <> 0` predicate
        // would silently drop those rows too.
        //
        // product_with_desc: a SECOND LEFT JOIN to webloan.dbo.loan_product
        // on (ld.loan_product = lp.id_code) enriches each row with a
        // human-readable description, producing
        //   ld.loan_product + ' - ' + lp.description
        // for the UI ("C35 - Quick Loan", etc.). This avoids the
        // N+1 round-trips the pending-loan endpoint pays through
        // GetLoanProductByIdCodeAsync — the join is cheap (small
        // reference table, primary key on id_code) and lets the
        // outstanding-loans list be returned in a single SQL execution.
        //
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
            ORDER BY ld.date_granted DESC";

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        // Materialize via the dedicated projection entity. AsNoTracking is
        // implied by the context's default (QueryTrackingBehavior.NoTracking
        // is set on the WebLoanDbContext); set explicitly for clarity.
        return await context.OutstandingLoanRows
            .FromSqlInterpolated(sql)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    // ─── Pending loans (pre_loan_data) ─────────────────────────────────
    public async Task<IReadOnlyList<PreLoanData>> GetPendingLoansAsync(
        string branchCode,
        string accountNo,
        CancellationToken ct = default)
    {
        // Same shape as GetOutstandingLoansAsync: (bch, acct_no) is an
        // exact match from the URL's combined `accountId` parameter.
        // All four workflow dates NULL → "in flight" (prepared, not yet
        // approved/released/voided). No UDF here; this is plain
        // LINQ-renderable SQL.
        //
        // Project ONLY identifiers + workflow dates. Underwriter-facing
        // columns (principal, granted_rate, total_amortization,
        // loan_product, cat_loan_purpose) live on loan_data, not
        // pre_loan_data — projecting them here would raise
        // "Invalid column name" from SQL Server. The service layer
        // composes those via GetLoanDataByLoanNoAsync using LoanNo
        // returned from each row.
        //
        // Ordered deterministically by (BranchCode, AccountNo, LoanNo)
        // so the same set comes back in the same order on repeat calls
        // — the schema permits duplicates for (bch, acct_no) and
        // "FirstOrDefault" would silently pick a different one each call.
        FormattableString sql = $@"
            SELECT
                bk, bch, acct_no, loan_no,
                prepared_date, approved_date, released_date, void_date
            FROM webloan.dbo.pre_loan_data
            WHERE acct_no = {accountNo}
              AND bch     = {branchCode}
              AND approved_date IS NULL
              AND prepared_date IS NULL
              AND released_date IS NULL
              AND void_date IS NULL
            ORDER BY bch, acct_no, loan_no";

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        return await context.PreLoanDatas
            .FromSqlInterpolated(sql)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<LoanData?> GetLoanDataByLoanNoAsync(
        string loanNo,
        string branchCode,
        string accountNo,
        CancellationToken ct = default)
    {
        // Matches the pre_loan_data → loan_data JOIN keys from the
        // original sample SQL: (loan_no, acct_no, bch). All three are
        // taken from the URL (bch via the combined accountId split).
        //
        // No TOP(1) needed — (loan_no, acct_no, bch) is a near-unique
        // combination in webloan (one ledger row per PN). But we still
        // use FirstOrDefaultAsync because the schema allows duplicates
        // in theory (e.g. a rebooked account); determinism beats
        // surprise.
        FormattableString sql = $@"
            SELECT TOP (1)
                bk, bch, acct_no, loan_no,
                loan_product, payment_interval, total_amortization,
                granted_rate, effective_rate, cat_loan_purpose,
                principal, applied_principal,
                principal_bal, amort_amount, over_bal,
                date_granted, date_maturity, loan_status,
                close_date, creation_type
            FROM webloan.dbo.loan_data
            WHERE loan_no   = {loanNo}
              AND acct_no   = {accountNo}
              AND bch       = {branchCode}";

        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.LoanDatas
            .FromSqlInterpolated(sql)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);
    }

    public async Task<LoanProductLookup?> GetLoanProductByIdCodeAsync(string idCode, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.LoanProducts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.IdCode == idCode, ct);
    }

    public async Task<LoanPurpose?> GetLoanPurposeByPathAsync(string path, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.LoanPurposes
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Path == path, ct);
    }

    public async Task<CheckListData?> GetNthpAsync(string cisNo, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.CheckListDatas
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.CisNo == cisNo && c.CheckListItem == CheckListData.NthpItem,
                ct);
    }
}
using EBI.ALAS.Api.Features.WebLoans;

namespace EBI.ALAS.Api.Features.WebLoans;

// Repository contract
// All repository methods are pure DB accessors — they do NOT know about
// the authenticated user. Branch scoping is applied at the service layer
// (which receives the JWT-derived bch) so the repository stays trivially
// testable and reusable for admin paths in the future.
public interface IWebLoanRepository
{
    Task<CisInfo?> GetCisInfoAsync(string cisNo, CancellationToken ct = default);

    Task<CisInfoMiscData?> GetAgencyTypeAsync(string cisNo, CancellationToken ct = default);

    // Customer-info enrichment
    // Resolves the CCR10 row (hire date / length-of-service source) for
    // a CIS. Returns null if no CCR10 row is recorded.
    Task<CheckListData?> GetLengthOfServiceAsync(string cisNo, CancellationToken ct = default);

    // Resolves a description by mis_group.id_code (used for the agency-
    // type join on cis_info_misc_data.value_str).
    Task<MisGroup?> GetMisGroupByIdCodeAsync(string idCode, CancellationToken ct = default);

    // Resolves descriptions by mis_group.path with no group_no filter
    // (used for the cat_mis_group2 join on loan_acct_info).
    Task<IReadOnlyList<MisGroup>> GetMisGroupsByPathsAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct = default);

    // Resolves descriptions by (mis_group.path, mis_group.group_no = 2)
    // (used for the solicitor join on loan_acct_info).
    Task<IReadOnlyList<MisGroup>> GetSolicitorsByPathsAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct = default);

    // Returns all accounts for a CIS, optionally scoped to a branch.
    // When branchCode is non-null (non-Admin callers), only accounts
    // belonging to that branch are returned. When null (Admin), all
    // branches are visible. This prevents encoders from seeing accounts
    // belonging to other branches for the same CIS.
    Task<IReadOnlyList<LoanAcctInfo>> GetAccountsByCisAsync(
        string cisNo,
        string? branchCode = null,
        CancellationToken ct = default);

    // Returns true when an account exists AND its cis_no matches the caller-
    // supplied cisNo AND its bch matches the caller-supplied branchCode.
    // Used to prevent cross-tenant enumeration on the outstanding-loans
    // endpoint (mirrors the README §546 /active-loans rule). The
    // (bch, acct_no) pair is the natural key of dbo.loan_acct_info, so
    // checking both is the cheapest correct ownership test.
    Task<bool> AccountBelongsToCisAsync(
        string cisNo,
        string branchCode,
        string accountNo,
        CancellationToken ct = default);

    // The (branchCode, accountNo) pair is taken from the URL's combined
    // `accountId` route parameter — caller-controlled. The repository
    // filters strictly on that pair; there is no JWT-derived bch fallback
    // (the Admin bypass / per-user branch scoping that used to live here
    // was removed when the endpoint moved to the combined-id model).
    // UDF filter is pushed into SQL via raw SQL because EF cannot
    // translate `webloan.dbo.is_loan(loan_no)`.
    // Returns all outstanding rows for the account, ordered by most
    // recent date_granted first. The original "active loans" query used
    // TOP (10) — replaced with parameterized OFFSET/FETCH so the UI
    // can paginate without us hydrating every historical row into
    // memory. Default cap of 50 keeps a single response small even for
    // accounts with hundreds of historical outstanding loans.
    // The returned rows are OutstandingLoanRow (keyless), not LoanData,
    // because the outstanding-loans query joins dbo.amort_data and
    // projects a derived `computed_amort_amount` column that does not
    // exist on dbo.loan_data — EF would reject any attempt to add it to
    // the LoanData entity. See OutstandingLoanRow.cs for the full
    // rationale.
    Task<IReadOnlyList<OutstandingLoanRow>> GetOutstandingLoansAsync(
        string branchCode,
        string accountNo,
        int pageSize = 50,
        int pageNumber = 1,
        CancellationToken ct = default);

    // Returns ALL in-flight pre_loan_data rows for (bch, acct_no) where
    // all four workflow dates are NULL — meaning each loan has been
    // prepared but not yet approved/released/voided. (branchCode,
    // accountNo) is taken from the URL's combined `accountId` parameter.
    // The single SQL execution LEFT JOINs against five lookup tables
    // (loan_data, loan_product, loan_purpose, loan_acct_info,
    // check_list_data) and projects three derived expressions
    // (creation_type_label, total_term_days, product_with_desc). The
    // returned `PendingLoanRow` is a keyless projection entity — see
    // PendingLoanRow.cs for the column-by-column rationale.
    // Returns an empty list (NOT null) when no in-flight rows exist —
    // the service distinguishes "no pending loan" from "account not
    // found" via AccountBelongsToCisAsync. Ordered deterministically by
    // (bch, acct_no, loan_no) so repeated calls return rows in the same
    // order — the schema permits duplicates for the same (bch,
    // acct_no) and "FirstOrDefault" would silently pick a different one
    // each call.
    // Replaces the previous N+1 fan-out (1 pre_loan_data + N loan_data
    // + N loan_product + N loan_purpose + 1 NTHP round-trips per
    // pending-loan response). For an account with N in-flight loans,
    // wall-time is one DB round-trip, not 3N+2.
    Task<IReadOnlyList<PendingLoanRow>> GetPendingLoansAsync(
        string branchCode,
        string accountNo,
        CancellationToken ct = default);

    // Active loan products (lookup)
    // Returns every row in dbo.loan_product WHERE expiration IS NULL —
    // i.e. products that have not been retired. Projects only id_code
    // and description (per spec). The pending-loan flow no longer uses
    // GetLoanProductByIdCodeAsync — it gets the product description via
    // a SQL LEFT JOIN inside the consolidated pending-loan query.
    // Ordered by id_code ascending so the response is deterministic and
    // dropdowns render in a stable order across calls.
    Task<IReadOnlyList<LoanProductLookup>> GetActiveLoanProductsAsync(CancellationToken ct = default);

    // All loan products (sync)
    // Returns EVERY row in dbo.loan_product, both active and retired,
    // including the `expiration` column. Used by the
    // LoanProductSyncService to mirror webloan's catalog into ALAS:
    //   * Newly-retired rows get IsRetired=true in the ALAS mirror.
    //   * Newly-active rows get IsRetired=false.
    //   * The full entity (id_code, description, expiration) is the
    //     source of truth; policy fields are NOT mirrored — ALAS owns
    //     those and the sync leaves them alone.
    // Returns the full LoanProductLookup rows (not a DTO) so the sync
    // can read Expiration without a second round-trip. Ordered by
    // id_code ascending.
    Task<IReadOnlyList<LoanProductLookup>> GetAllLoanProductsAsync(CancellationToken ct = default);

    // Resolves a single `cat_loan_class` value for the composite key
    // (branchCode, loanNo, productCode) in dbo.loan_data. All three
    // inputs are caller-supplied — there is no JWT-derived branch
    // fallback or default.
    // Returns null when no matching row exists (SQL NULL from TOP 0 or
    // DBNull from ExecuteScalar). The service layer translates null → 404
    // so the caller can distinguish "loan not found" from "loan found
    // but cat_loan_class IS NULL" — both render the same placeholder in
    // the UI, which is the right UX.
    // Uses raw ADO.NET (DbConnection) to bypass EF Core's property
    // mapper entirely. This is critical because:
    //   * The webloan loan_data table may not expose cat_loan_class as
    //     a real column in all deployments (it may be a joined/computed
    //     field depending on the webloan schema version).
    //   * EF Core's FromSql on keyless entities wraps raw SQL in a
    //     subquery that selects all mapped properties — causing
    //     "Invalid column name" for columns that aren't in the DB.
    //   * Raw ADO.NET ExecuteScalar reads only the one explicitly-named
    //     result column, so no EF mapping occurs.
    // All parameters are sent via DbParameter — no SQL injection.
    Task<string?> GetCatLoanClassAsync(
        string branchCode,
        string loanNo,
        string productCode,
        CancellationToken ct = default);

    // COCREE completion check
    // Returns the CCR01–CCR11 rows for a CIS from dbo.check_list_data.
    // Uses the composite PK index (cis_no, check_list_item) for a
    // single seek + range scan — returns ≤11 rows (~1KB).
    // Returns an empty list (NOT null) when no rows exist — the service
    // treats that as "all items incomplete".
    Task<IReadOnlyList<CheckListData>> GetCocreeItemsAsync(
        string cisNo,
        CancellationToken ct = default);
}
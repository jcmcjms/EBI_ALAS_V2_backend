using EBI.ALAS.Api.Features.WebLoans;

namespace EBI.ALAS.Api.Features.WebLoans;

// ─── Repository contract ─────────────────────────────────────────────────
// All repository methods are pure DB accessors — they do NOT know about
// the authenticated user. Branch scoping is applied at the service layer
// (which receives the JWT-derived bch) so the repository stays trivially
// testable and reusable for admin paths in the future.
public interface IWebLoanRepository
{
    // ─── CIS search ──────────────────────────────────────────────────────
    Task<CisInfo?> GetCisInfoAsync(string cisNo, CancellationToken ct = default);

    Task<CisInfoMiscData?> GetAgencyTypeAsync(string cisNo, CancellationToken ct = default);

    // ─── Customer-info enrichment ─────────────────────────────────────
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

    Task<IReadOnlyList<LoanAcctInfo>> GetAccountsByCisAsync(string cisNo, CancellationToken ct = default);

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

    // ─── Outstanding loans ───────────────────────────────────────────────
    // The (branchCode, accountNo) pair is taken from the URL's combined
    // `accountId` route parameter — caller-controlled. The repository
    // filters strictly on that pair; there is no JWT-derived bch fallback
    // (the Admin bypass / per-user branch scoping that used to live here
    // was removed when the endpoint moved to the combined-id model).
    //
    // UDF filter is pushed into SQL via raw SQL because EF cannot
    // translate `webloan.dbo.is_loan(loan_no)`.
    //
    // Returns all outstanding rows for the account, ordered by most
    // recent date_granted first. The original "active loans" query used
    // TOP (10) — replaced with parameterized OFFSET/FETCH so the UI
    // can paginate without us hydrating every historical row into
    // memory. Default cap of 50 keeps a single response small even for
    // accounts with hundreds of historical outstanding loans.
    //
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

    // ─── Pending loans (pre_loan_data) ─────────────────────────────────
    // Returns ALL in-flight pre_loan_data rows for (bch, acct_no) where
    // all four workflow dates are NULL — meaning each loan has been
    // prepared but not yet approved/released/voided. (branchCode,
    // accountNo) is taken from the URL's combined `accountId` parameter.
    //
    // Returns an empty list (NOT null) when no in-flight rows exist —
    // the service distinguishes "no pending loan" from "account not
    // found" via AccountBelongsToCisAsync. Ordered deterministically by
    // (BranchCode, AccountNo, LoanNo) so repeated calls return rows in
    // the same order — the schema permits duplicates for the same
    // (bch, acct_no) and "FirstOrDefault" would silently pick a
    // different one each call.
    Task<IReadOnlyList<PreLoanData>> GetPendingLoansAsync(
        string branchCode,
        string accountNo,
        CancellationToken ct = default);

    // The original SQL joins pre_loan_data → loan_data on
    // (loan_no, acct_no, bch) to surface underwriter-facing fields:
    // principal, granted_rate, total_amortization, loan_product,
    // cat_loan_purpose. Returns null if no matching loan_data row
    // exists (LEFT JOIN semantics — fields stay null in that case).
    Task<LoanData?> GetLoanDataByLoanNoAsync(
        string loanNo,
        string branchCode,
        string accountNo,
        CancellationToken ct = default);

    // ─── Pending-loan enrichment lookups ──────────────────────────────
    // Single-row lookups keyed by the join columns in the pending-loan
    // query. Service composes them in parallel after fetching the
    // pre_loan_data row.
    Task<LoanProductLookup?> GetLoanProductByIdCodeAsync(string idCode, CancellationToken ct = default);
    Task<LoanPurpose?> GetLoanPurposeByPathAsync(string path, CancellationToken ct = default);

    // CCR07 row for the loan_acct_info.cis_no — carries NTHP amount
    // (description) and NTHP date (expiration).
    Task<CheckListData?> GetNthpAsync(string cisNo, CancellationToken ct = default);
}
namespace EBI.ALAS.Api.Features.WebLoans;

// Service contract for the read-only WebLoan drill-down flow.
//
// Branch scoping rules differ per endpoint:
//   * `SearchByCisAsync` — `bch` is the auth user's branch (null for Admin).
//     Non-Admin callers (e.g. Encoder) are scoped to their branch so they
//     only see accounts belonging to their branch. Admin sees all branches.
//   * `GetOutstandingLoansAsync` / `GetPendingLoanAsync` — `bch` comes
//     from the URL (the `accountId` route parameter), NOT from the JWT.
//     The caller controls it; the JWT no longer restricts branch scope
//     for these two endpoints. See WebLoanEndpoints for the rationale.
public interface IWebLoanService
{
    Task<CisSearchResponse?> SearchByCisAsync(
        string cisNo,
        string? bch,
        CancellationToken ct = default);

    // `accountId` is the combined "<branchCode>-<accountNo>" form, e.g.
    // "011-05-13081-1" — see WebLoanAccountId.Parse. The service splits
    // it and passes the two halves down to the repository.
    //
    // Pagination defaults match the original TOP (10) behaviour for
    // accounts with a handful of active loans, while letting the UI
    // ask for more when needed. Hard ceiling on pageSize is enforced
    // by the endpoint layer — the service trusts its caller.
    Task<OutstandingLoansResponse?> GetOutstandingLoansAsync(
        string cisNo,
        string accountId,
        int pageSize = 50,
        int pageNumber = 1,
        CancellationToken ct = default);

    // Pending loan application context for an account. Returns null when
    // no in-flight pre_loan_data row exists OR when the (cisNo, accountId)
    // pair fails the anti-enumeration guard (mirrors the outstanding-loans
    // endpoint's behavior).
    Task<PendingLoanResponse?> GetPendingLoanAsync(
        string cisNo,
        string accountId,
        CancellationToken ct = default);

    // Active loan products lookup — surfaces products from dbo.loan_product
    // where expiration IS NULL. Projects only id_code + description; the
    // service is the seam for trimming webloan's column shape to the API
    // contract. Returns an empty list (NOT null) when no active rows
    // exist, mirroring how GetOutstandingLoansAsync handles empty pages.
    Task<IReadOnlyList<LoanProductDto>> GetActiveLoanProductsAsync(CancellationToken ct = default);

    // ─── Loan class lookup ────────────────────────────────────────────────
    // Resolves `cat_loan_class` from dbo.loan_data for the composite key
    // (bch, loan_no, loan_product). All three are caller-supplied via
    // query string — no JWT-derived branch fallback.
    //
    // Returns null when no matching row exists. The endpoint maps null
    // to 404, distinguishing "loan not in webloan" from "loan in webloan
    // but cat_loan_class IS NULL" (both surface the same null — the UI
    // renders a placeholder in both cases, which is the right UX).
    Task<CatLoanClassResponse?> GetCatLoanClassAsync(
        string bch,
        string loanNo,
        string loanProduct,
        CancellationToken ct = default);
}
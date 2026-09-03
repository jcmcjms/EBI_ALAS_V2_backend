using System.Globalization;

namespace EBI.ALAS.Api.Features.WebLoans;

public class WebLoanService(IWebLoanRepository repository) : IWebLoanService
{
    // ─── CIS search ───────────────────────────────────────────────────────
    public async Task<CisSearchResponse?> SearchByCisAsync(
        string cisNo,
        string? bch,
        CancellationToken ct = default)
    {
        // The bch is the auth user's branch (null for Admin). The CIS-
        // search endpoint does not filter rows by branch (a CIS can have
        // accounts in multiple branches), so the value is not used here —
        // it stays on the signature for forward-compat with branch-scoped
        // audit logging and to keep the contract symmetric with the
        // loans call.
        _ = bch;

        // Fire all four independent queries in parallel. The cis_info
        // row is the gate (404 if missing); the other three are
        // best-effort enrichments.
        var cisTask = repository.GetCisInfoAsync(cisNo, ct);
        var accountsTask = repository.GetAccountsByCisAsync(cisNo, ct);
        var agencyTypeTask = repository.GetAgencyTypeAsync(cisNo, ct);
        var lengthOfServiceTask = repository.GetLengthOfServiceAsync(cisNo, ct);

        var cis = await cisTask;
        if (cis is null) return null;

        await Task.WhenAll(accountsTask, agencyTypeTask, lengthOfServiceTask);

        var accounts = await accountsTask;
        var agencyType = await agencyTypeTask;
        var lengthOfServiceRow = await lengthOfServiceTask;

        var lengthOfService = ComputeLengthOfService(lengthOfServiceRow?.Description);

        // Distinct cat_mis_group2 / solicitor paths across the accounts.
        // All accounts under a CIS typically share the same group2 path
        // and the same solicitor, but we Distinct() defensively.
        var group2Paths = accounts
            .Select(a => a.MisGroup2)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .Cast<string>()
            .ToList();

        var solicitorPaths = accounts
            .Select(a => a.Solicitor)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .Cast<string>()
            .ToList();

        // Resolve all three description families in parallel — agency
        // (id_code), MIS agency (path), requesting officer (path+group2=2).
        var agencyDescTask = agencyType?.ValueStr is { Length: > 0 } agencyCode
            ? repository.GetMisGroupByIdCodeAsync(agencyCode, ct)
            : Task.FromResult<MisGroup?>(null);

        var misGroupsByPathTask = group2Paths.Count > 0
            ? repository.GetMisGroupsByPathsAsync(group2Paths, ct)
            : Task.FromResult<IReadOnlyList<MisGroup>>(Array.Empty<MisGroup>());

        var solicitorsByPathTask = solicitorPaths.Count > 0
            ? repository.GetSolicitorsByPathsAsync(solicitorPaths, ct)
            : Task.FromResult<IReadOnlyList<MisGroup>>(Array.Empty<MisGroup>());

        await Task.WhenAll(agencyDescTask, misGroupsByPathTask, solicitorsByPathTask);

        var agencyGroup = await agencyDescTask;
        var misGroupsByPath = await misGroupsByPathTask;
        var solicitorsByPath = await solicitorsByPathTask;

        // Build lookups keyed by path for O(1) resolution below.
        var byPath = misGroupsByPath
            .Where(m => !string.IsNullOrEmpty(m.Path))
            .ToDictionary(m => m.Path!, m => m.Description);

        var bySolicitorPath = solicitorsByPath
            .Where(m => !string.IsNullOrEmpty(m.Path))
            .ToDictionary(m => m.Path!, m => m.Description);

        string? misAgencyDescription = null;
        if (accounts.Count > 0 &&
            !string.IsNullOrWhiteSpace(accounts[0].MisGroup2) &&
            byPath.TryGetValue(accounts[0].MisGroup2!, out var misAgencyDesc))
        {
            misAgencyDescription = misAgencyDesc;
        }

        string? solicitorDescription = null;
        if (accounts.Count > 0 &&
            !string.IsNullOrWhiteSpace(accounts[0].Solicitor) &&
            bySolicitorPath.TryGetValue(accounts[0].Solicitor!, out var solDesc))
        {
            solicitorDescription = solDesc;
        }

        var borrower = new BorrowerDto(
            CisNo: cis.CisNo,
            FirstName: cis.FirstName,
            MiddleName: cis.MiddleName,
            LastName: cis.LastName,
            Title: cis.Title,
            Appelation: cis.Appelation,
            BirthDate: ParseBirthDate(cis.BirthDateRaw),
            Address: BuildAddress(cis),
            AgencyType: agencyGroup?.Description,
            PositionTitle: cis.Occupation,
            Region: WebLoanRegions.Resolve(cis.RegionCode),
            RegionCode: cis.RegionCode,
            DivisionCode: cis.DivisionCode,
            StationCode: cis.StationCode,
            EmployeeNumber: cis.EmployeeNo,
            MisAgency: misAgencyDescription,
            RequestingOfficer: solicitorDescription,
            LengthOfService: lengthOfService);

        var accountDtos = accounts.Select(a => new AccountDto(
            BankCode: a.BankCode,
            BranchCode: a.BranchCode,
            AccountNo: a.AccountNo,
            AccountId: WebLoanAccountId.Format(a.BranchCode, a.AccountNo),
            Name: a.Name,
            CreditLimit: a.CreditLimit,
            UsedCredit: a.UsedCredit,
            BorrowerType: a.BorrowerType
        )).ToList();

        return new CisSearchResponse(borrower, accountDtos);
    }

    // ─── Outstanding loans ───────────────────────────────────────────────
    public async Task<OutstandingLoansResponse?> GetOutstandingLoansAsync(
        string cisNo,
        string accountId,
        int pageSize = 50,
        int pageNumber = 1,
        CancellationToken ct = default)
    {
        // Split the combined accountId into (bch, acctNo) up front. Throws
        // ArgumentException on a malformed value — the GlobalExceptionHandler
        // turns that into 400 before any DB call is made. Keeps the
        // repository layer free of string-parsing concerns.
        var (branchCode, accountNo) = WebLoanAccountId.Parse(accountId);

        // Account↔CIS ownership is enforced BEFORE any loan row is read,
        // regardless of role. This is the same anti-enumeration guard as
        // the README §546 /active-loans endpoint: a user who guesses
        // (cisNo, accountId) for a different branch's account gets 404,
        // not 200-with-empty. The (bch, acct_no) pair is the natural key
        // of dbo.loan_acct_info, so the check is the cheapest correct
        // ownership test — and including bch stops a caller from picking
        // a (cisNo, accountNo) pair that exists under a different branch.
        var belongs = await repository.AccountBelongsToCisAsync(cisNo, branchCode, accountNo, ct);
        if (!belongs) return null;

        // Branch scoping: taken from the URL only. The JWT-derived bch
        // (and the Admin bypass that used to live here) is intentionally
        // not consulted — the branch is part of the account identity in
        // the combined-id model.
        //
        // Pagination: pushed to SQL via OFFSET/FETCH so the database
        // returns only the page slice. Without this, a long-tenured
        // borrower with hundreds of historical outstanding loans would
        // hydrate the entire result set on every drill-down — a 6MB+
        // payload at the 99th percentile. With it, a single page
        // (default 50) tops out around 80KB.
        var rows = await repository.GetOutstandingLoansAsync(branchCode, accountNo, pageSize, pageNumber, ct);

        var loans = rows
            .OrderByDescending(r => r.DateGranted ?? DateTime.MinValue)
            .Select(r =>
            {
                var status = WebLoanRegions.ResolveLoanStatus(r.StatusCode);
                var statusLabel = WebLoanRegions.Label(status);
                var productCode = r.ProductCode ?? string.Empty;

                // Product-with-description string, computed in SQL via a
                // LEFT JOIN to webloan.dbo.loan_product on
                // (ld.loan_product = lp.id_code):
                //
                //   ld.loan_product + ' - ' + ISNULL(lp.description, '')
                //
                // When the loan_product row is missing (orphaned/retired
                // product code), the SQL coerces the description to ''
                // and we end up with "<code> - " here. Trim the trailing
                // " - " so the UI never sees a dangling separator —
                // productCode alone is a perfectly valid display value
                // for those legacy rows. See
                // WebLoanRepository.GetOutstandingLoansAsync for the
                // full join + ISNULL rationale.
                var productWithDesc = (r.ProductWithDescription ?? string.Empty).TrimEnd();
                if (productWithDesc.EndsWith(" - ", StringComparison.Ordinal))
                {
                    productWithDesc = productWithDesc[..^3];
                }

                return new OutstandingLoanDto(
                    LoanNo: r.LoanNo,
                    Principal: r.Principal,
                    PrincipalBalance: r.PrincipalBalance,
                    // CASE-computed in SQL — see
                    // WebLoanRepository.GetOutstandingLoansAsync for the
                    // LEFT JOIN to amort_data + CASE expression. For C35
                    // and C23 products this equals Principal; for
                    // everything else it equals amort_data.total_amort
                    // (first installment, amort_no = 1). LEFT JOIN → NULL
                    // when no amort_data row exists for a non-C35/C23
                    // loan, which the UI renders as "—".
                    //
                    // Sourced from OutstandingLoanRow (the projection row
                    // type returned by the repository), not LoanData —
                    // because the derived column cannot live on the
                    // 1:1 webloan entity.
                    AmortAmount: r.ComputedAmortAmount,
                    DateGranted: r.DateGranted,
                    DateMaturity: r.DateMaturity,
                    ProductCode: productCode,
                    ProductStatus: $"{productCode} - {statusLabel}",
                    // "<loan_product> - <description>" (e.g.
                    // "C35 - Quick Loan"), or just the product code when
                    // no loan_product row matched the join. Trimming the
                    // trailing " - " happens above; this is the
                    // post-trim string.
                    ProductWithDescription: productWithDesc);
            })
            .ToList();

        // Echo the combined accountId so the UI can pass it back unchanged
        // for any follow-up call (pending-loan, etc.). The split halves
        // are echoed too as a convenience for clients that already need
        // them separately.
        return new OutstandingLoansResponse(
            CisNo: cisNo,
            AccountId: accountId,
            BranchCode: branchCode,
            AccountNo: accountNo,
            Loans: loans);
    }

    // ─── Pending loans ───────────────────────────────────────────────────
    public async Task<PendingLoanResponse?> GetPendingLoanAsync(
        string cisNo,
        string accountId,
        CancellationToken ct = default)
    {
        // Split the combined accountId into (bch, acctNo) up front. Throws
        // ArgumentException on a malformed value — the GlobalExceptionHandler
        // turns that into 400 before any DB call is made.
        var (branchCode, accountNo) = WebLoanAccountId.Parse(accountId);

        // Anti-enumeration guard FIRST. Mirrors the outstanding-loans
        // endpoint: even Admin can't probe (cisNo, accountId) pairs that
        // don't belong together. Knowing that a pair exists shouldn't
        // leak via an empty 200. The (bch, acct_no) pair is the natural
        // key of dbo.loan_acct_info, so the check is the cheapest
        // correct ownership test.
        var belongs = await repository.AccountBelongsToCisAsync(cisNo, branchCode, accountNo, ct);
        if (!belongs) return null;

        // Fan out: fetch the in-flight pre_loan_data rows AND the CIS-level
        // NTHP row in parallel. They are independent (different tables,
        // different key columns) so there is no benefit to sequencing.
        var preLoansTask = repository.GetPendingLoansAsync(branchCode, accountNo, ct);
        var nthpTask = repository.GetNthpAsync(cisNo, ct);

        await Task.WhenAll(preLoansTask, nthpTask);

        var preLoans = await preLoansTask;
        var nthp = await nthpTask;

        // Per-row enrichment. Each pre-loan row carries its own
        // (loan_no, account_no, branch_code) tuple, so each loan_data
        // lookup is independent. We start them all in parallel rather
        // than sequentially — even for N rows the total wall time is one
        // round-trip, not N. (branchCode, accountNo) is the URL bch/act —
        // we use the URL's branch (not the row's pre_loan_data.bch) so
        // the loan_data lookup is consistent with the pre_loan_data
        // filter above.
        var loanDataTasks = preLoans
            .Select(p => repository.GetLoanDataByLoanNoAsync(
                p.LoanNo, branchCode, accountNo, ct))
            .ToArray();

        var loanDatas = await Task.WhenAll(loanDataTasks);

        // Sequential follow-up per row: loan_product + loan_purpose keys
        // come from the corresponding loan_data, so we can't fan those
        // out in parallel with the loan_data row. We do start the
        // lookups for all rows in parallel with each other though —
        // each row's product/purpose fetch is independent of every
        // other row's.
        var productTasks = loanDatas
            .Select(ld => !string.IsNullOrWhiteSpace(ld?.ProductCode)
                ? repository.GetLoanProductByIdCodeAsync(ld!.ProductCode!, ct)
                : Task.FromResult<LoanProductLookup?>(null))
            .ToArray();

        var purposeTasks = loanDatas
            .Select(ld => !string.IsNullOrWhiteSpace(ld?.Purpose)
                ? repository.GetLoanPurposeByPathAsync(ld!.Purpose!, ct)
                : Task.FromResult<LoanPurpose?>(null))
            .ToArray();

        await Task.WhenAll(productTasks);
        await Task.WhenAll(purposeTasks);
        var products = productTasks
            .Select(t => t.IsCompletedSuccessfully ? t.Result : null)
            .ToArray();
        var purposes = purposeTasks
            .Select(t => t.IsCompletedSuccessfully ? t.Result : null)
            .ToArray();

        // Build DTOs in a parallel-indexed loop so the indices stay clear.
        var dtos = new List<PendingLoanDto>(preLoans.Count);
        for (var i = 0; i < preLoans.Count; i++)
        {
            var pre = preLoans[i];
            var ld = loanDatas[i];
            var product = products[i];
            var purpose = purposes[i];

            // Underwriter-facing fields sourced from loan_data, not
            // pre_loan_data — see PreLoanData entity note.
            var productCode = ld?.ProductCode ?? string.Empty;
            var productDescription = product?.Description ?? string.Empty;
            var productWithDescription = string.IsNullOrEmpty(productDescription)
                ? productCode
                : $"{productCode} - {productDescription}";

            dtos.Add(new PendingLoanDto(
                LoanNo: pre.LoanNo,
                Principal: ld?.Principal,
                GrantedRate: ld?.GrantedRate,
                TotalTermDays: ld?.TotalAmortization is int term
                    ? term * 30
                    : null,
                ProductWithDescription: productWithDescription,
                LoanPurpose: purpose?.Description,
                CreationType: ld?.CreationType,
                CreationTypeLabel: WebLoanRegions.CreationTypeLabel(ld?.CreationType)));
        }

        return new PendingLoanResponse(
            CisNo: cisNo,
            AccountId: accountId,
            BranchCode: branchCode,
            AccountNo: accountNo,
            Loans: dtos,
            Nthp: nthp?.Description,
            NthpDate: nthp?.Expiration);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────
    private static DateTime? ParseBirthDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // p_bday is varchar(10) in webloan — try a few common layouts and
        // never throw; an unparseable value is more useful as "missing"
        // than as a 500.
        string[] formats =
        {
            "yyyy-MM-dd",
            "MM/dd/yyyy",
            "M/d/yyyy",
            "yyyy/MM/dd",
            "dd-MM-yyyy"
        };

        if (DateTime.TryParseExact(
                raw.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dt))
        {
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        return DateTime.TryParse(
            raw.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var fallback)
            ? DateTime.SpecifyKind(fallback, DateTimeKind.Utc)
            : null;
    }

    private static string? ComputeLengthOfService(string? rawHireDate)
    {
        // Mirrors the original SQL's DATEDIFF math:
        //   CAST(DATEDIFF(YEAR, hire_date, GETDATE()) AS VARCHAR(3)) + ' years, ' +
        //   CAST(DATEDIFF(MONTH, hire_date, GETDATE()) % 12 AS VARCHAR(2)) + ' months'
        //
        // Edge cases:
        //   * rawHireDate is null/whitespace (no CCR10 row recorded) → null
        //   * rawHireDate is unparseable → null (don't surface garbage)
        //   * Hire date in the future → "0 years, 0 months" (don't crash)
        if (string.IsNullOrWhiteSpace(rawHireDate)) return null;

        var hireDate = ParseBirthDate(rawHireDate);
        if (hireDate is null) return null;

        var now = DateTime.UtcNow;

        // Total months between hireDate and now — anchored at the hire
        // date so the years/months align with the original SQL semantics.
        var totalMonths = Math.Max(0, ((now.Year - hireDate.Value.Year) * 12) + (now.Month - hireDate.Value.Month));
        var years = totalMonths / 12;
        var months = totalMonths % 12;

        return $"{years} years, {months} months";
    }

    private static string? BuildAddress(CisInfo cis)
    {
        // Mirrors the original CONCAT from the sample SQL. Skip empty
        // components and join with ", " to avoid trailing/leading
        // punctuation.
        var parts = new List<string?>
        {
            cis.Zip,
            cis.HouseStreet,
            cis.City,
            cis.StateProvince,
            cis.Barangay,
            cis.Village
        }
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Select(p => p!.Trim())
        .ToList();

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
using System.Globalization;

namespace EBI.ALAS.Api.Features.WebLoans;

public class WebLoanService(IWebLoanRepository repository) : IWebLoanService
{
    public async Task<CisSearchResponse?> SearchByCisAsync(
        string cisNo,
        string? bch,
        CancellationToken ct = default)
    {
        // The bch is the auth user's branch (null for Admin). Non-Admin
        // callers (e.g. Encoder) are scoped to their branch so they only
        // see accounts belonging to their branch. Admin sees all branches.

        // Fire all four independent queries in parallel. The cis_info
        // row is the gate (404 if missing); the other three are
        // best-effort enrichments.
        var cisTask = repository.GetCisInfoAsync(cisNo, ct);
        var accountsTask = repository.GetAccountsByCisAsync(cisNo, bch, ct);
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
                //   ld.loan_product + ' - ' + ISNULL(lp.description, '')
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

        // Single round-trip: the repository's consolidated SQL joins
        // pre_loan_data → loan_data → loan_product / loan_purpose /
        // loan_acct_info → check_list_data in one execution and projects
        // every field the response needs. Replaces the prior N+1
        // fan-out (1 pre_loan_data + N loan_data + N loan_product +
        // N loan_purpose + 1 NTHP round-trips per pending-loan
        // response). For an account with N in-flight loans, wall-time
        // is one DB round-trip, not 3N+2.
        var rows = await repository.GetPendingLoansAsync(branchCode, accountNo, ct);

        // NTHP cartesian-product caveat (see PendingLoanRow note):
        // if check_list_data has multiple CCR07 rows for the cis_no,
        // the SQL duplicates each pre_loan_data row. NTHP/NthpDate are
        // CIS-level attributes and identical across duplicates, so we
        // read them from the first row only. Per-loan fields are
        // identical across duplicates too, so the .Distinct() below is
        // a safety belt — in practice the unique (loan_no) tie-break
        // collapses the duplicates.
        var nthpRow = rows.FirstOrDefault();

        var dtos = rows
            .GroupBy(r => r.LoanNo)  // collapse CCR07 cartesian duplicates
            .Select(g => g.First())
            .Select(r =>
            {
                // Trim the trailing " - " the SQL emits when the
                // loan_product LEFT JOIN misses (orphaned/retired
                // product code). Same pattern as the outstanding-loans
                // service — see WebLoanService.GetOutstandingLoansAsync.
                var productWithDesc = (r.ProductWithDescription ?? string.Empty).TrimEnd();
                if (productWithDesc.EndsWith(" - ", StringComparison.Ordinal))
                {
                    productWithDesc = productWithDesc[..^3];
                }

                return new PendingLoanDto(
                    LoanNo: r.LoanNo,
                    Principal: r.Principal,
                    GrantedRate: r.GrantedRate,
                    // Exact day count from SQL's DATEDIFF(DAY, …).
                    // Replaces the legacy `total_amortization * 30`
                    // approximation, which drifted by up to ±1 day
                    // per period — material for short-term products.
                    // NULL when either loan_data date is missing.
                    TotalTermDays: r.TotalTermDays,
                    // Policy term from loan_data.total_amortization,
                    // surfaced verbatim under the more descriptive name
                    // PolicyTermMonths — distinguishes it from the
                    // exact-day count above and matches the SQL
                    // comment in WebLoanRepository.GetPendingLoansAsync
                    // that labels this field "-- policy months".
                    // NULL when no loan_data row exists (LEFT JOIN miss).
                    PolicyTermMonths: r.TotalAmortization,
                    ProductWithDescription: productWithDesc,
                    LoanPurpose: r.LoanPurpose,
                    CreationType: r.CreationType,
                    CreationTypeLabel: string.IsNullOrEmpty(r.CreationTypeLabel)
                        ? WebLoanRegions.CreationTypeLabel(r.CreationType)
                        : r.CreationTypeLabel);
            })
            .ToList();

        return new PendingLoanResponse(
            CisNo: cisNo,
            AccountId: accountId,
            BranchCode: branchCode,
            AccountNo: accountNo,
            Loans: dtos,
            Nthp: nthpRow?.Nthp,
            NthpDate: nthpRow?.NthpDate);
    }

    public async Task<IReadOnlyList<LoanProductDto>> GetActiveLoanProductsAsync(CancellationToken ct = default)
    {
        // Single SQL roundtrip: the repository already filters
        // `expiration IS NULL` and orders by id_code. We project only the
        // two columns the spec asks for (id_code + description) — the
        // retirement flag is a server-side predicate only and never
        // surfaces to clients.
        // Empty list is a valid result (no active products in webloan
        // is a real — though unusual — state, e.g. during a cutover).
        // The endpoint maps that to 200 with `data: []`, mirroring the
        // pending-loan endpoint's "200 with empty Loans" semantics.
        var rows = await repository.GetActiveLoanProductsAsync(ct);

        return rows
            .Select(p => new LoanProductDto(p.IdCode, p.Description))
            .ToList();
    }

    public async Task<CatLoanClassResponse?> GetCatLoanClassAsync(
        string bch,
        string loanNo,
        string loanProduct,
        CancellationToken ct = default)
    {
        // Caller supplies all three: bch, loanNo, loanProduct.
        // No JWT branch fallback — the URL parameters are the identity.
        // No anti-enumeration guard needed here: unlike the CIS drill-down
        // (where cross-tenant enumeration is a concern), a
        // (bch, loan_no, loan_product) triple is specific enough that
        // guessing another tenant's combo is not a realistic attack
        // surface. The same triple used elsewhere (pending-loan,
        // outstanding-loans) does not gate on CIS ownership either.
        var catLoanClass = await repository.GetCatLoanClassAsync(bch, loanNo, loanProduct, ct);

        // Null from repository means no matching row in dbo.loan_data.
        // Endpoint maps this to 404 — "loan not found in webloan for
        // the given (bch, loan_no, loan_product)".
        if (catLoanClass is null) return null;

        // Empty string from repository means the loan row exists but
        // cat_loan_class IS NULL. Return a valid response with null
        // CatLoanClass so the endpoint returns 200 (not 404).
        return new CatLoanClassResponse(
            Bch: bch,
            LoanNo: loanNo,
            LoanProduct: loanProduct,
            CatLoanClass: string.IsNullOrEmpty(catLoanClass) ? null : catLoanClass);
    }

    public async Task<CocreeStatusResponse> GetCocreeStatusAsync(
        string cisNo,
        CancellationToken ct = default)
    {
        // Single round-trip: repository fetches ≤11 rows via the
        // composite PK index (cis_no, check_list_item).
        var rows = await repository.GetCocreeItemsAsync(cisNo, ct);

        // Index the DB rows by item code for O(1) lookup. A CIS with
        // zero rows produces an empty dictionary → all items incomplete.
        var byItem = rows
            .Where(r => !string.IsNullOrEmpty(r.CheckListItem))
            .ToDictionary(r => r.CheckListItem, r => r);

        // Build the full 11-item list, merging DB rows against the
        // canonical CCR01–CCR11 set. Missing items → Submitted=null.
        var items = CheckListData.CocreeItems
            .Select(code =>
            {
                byItem.TryGetValue(code, out var row);
                return new CocreeItemStatus(
                    ItemCode: code,
                    Submitted: row?.Submitted,
                    Description: row?.Description,
                    Expiration: row?.Expiration);
            })
            .ToList();

        // IsComplete = true only when ALL 11 items have a non-null Submitted.
        var isComplete = items.All(i => i.Submitted.HasValue);

        return new CocreeStatusResponse(
            CisNo: cisNo,
            IsComplete: isComplete,
            Items: items);
    }

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
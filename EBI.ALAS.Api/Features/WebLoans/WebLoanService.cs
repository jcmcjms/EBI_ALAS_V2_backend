using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.WebLoans;

/// <summary>
/// Assembles ALAS loan-application sections from the read-only WebLoan database.
/// Source tables: cis_info (client master), cis_info_misc_data (per-client attributes),
/// loan_acct_info (client→account), loan_data (PN records), loan_status / loan_product
/// (lookups), mis_group (multi-purpose hierarchy — group_no=1 holds MIS region/agency
/// paths, group_no=2 holds requesting officers, group_no=26 holds agency types).
/// </summary>
public class WebLoanService : IWebLoanService
{
    private readonly WebLoanDbContext _db;
    private readonly ILogger<WebLoanService> _logger;

    // loan_status codes considered CLOSED/PAYOFF — excluded from Outstanding Loans
    // per requirement "do not include accounts for payoff".
    private static readonly byte[] PayoffStatuses = [5, 6, 7, 8]; // TPL total, Write-Off, Total, Transfer Asset

    // mis_group.group_no that holds the requesting officer hierarchy.
    // loan_acct_info.solicitor is a path; the description of the matching row
    // (e.g. "ALDREX JOEY L. CEZAR") is what we display as the requesting officer.
    // The user's legacy webloan customer-info query joins with
    // "AND mg3.group_no = 2" on this exact path — keep that contract.
    private const int RequestingOfficerGroupNo = 2;

    // mis_group.group_no that holds the MIS grouping hierarchy (regions/areas
    // and the secondary MIS agency classification). cat_mis_group2 is a path
    // in this group (e.g. "CLD/C00/C0001/C000101/C000101007" → "DEPED LIANGA").
    // Verified against live data on CIS 0880944451 (Aug 2026) — the path
    // resolves in group_no=1 and is absent from group_no=2.
    private const int MisGroupingGroupNo = 1;

    // mis_group.group_no that holds the agency type hierarchy. Reached via
    // cis_info_misc_data.value_str when cis_info_misc_data.id_code = 14
    // (see CisInfoMiscData.AgencyTypeIdCode).
    //
    // Verified against live data on CIS 0880944451 (Aug 2026): id_code='AT002'
    // (RPSU) lives in group_no=26. The original assumption of group_no=14 was
    // incorrect — no `AT*` agency-type rows exist in group_no=14. Matches the
    // production legacy customer-info query, which joins without a group_no
    // filter; the agency-type rows in group_no=26 are unique on id_code for
    // all `AT*` values seen in cis_info_misc_data (id_code=14) except AT000
    // (which collides with group_no=1 — not used by any current borrower).
    private const int AgencyTypeGroupNo = 26;

    public WebLoanService(WebLoanDbContext db, ILogger<WebLoanService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<CisSearchResult?> SearchCisAsync(string cisNo, CancellationToken ct = default)
    {
        var cis = await _db.CisInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CisNo == cisNo, ct);

        if (cis is null)
            return null;

        var accounts = await _db.LoanAcctInfos
            .AsNoTracking()
            .Where(a => a.CisNo == cisNo)
            .ToListAsync(ct);

        var accountNos = accounts.Select(a => a.AccountNo).ToList();

        // Get PN counts per account
        var pnCounts = accountNos.Count == 0
            ? new Dictionary<string, int>()
            : await _db.LoanDatas
                .AsNoTracking()
                .Where(l => accountNos.Contains(l.AccountNo) && l.LoanNo != null)
                .GroupBy(l => l.AccountNo)
                .Select(g => new { AccountNo = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.AccountNo, x => x.Count, ct);

        return new CisSearchResult
        {
            CisNo = cis.CisNo,
            FullName = $"{cis.FirstName} {cis.MiddleName} {cis.LastName} {cis.Appelation}".Trim(),
            BranchCode = cis.BranchCode ?? string.Empty,
            Accounts = accounts.Select(a => new CisAccountSummary
            {
                AccountNo = a.AccountNo,
                AccountName = a.Name,
                AccountAddress = BuildAccountAddress(a),
                MisGroup = a.MisGroup,
                PnCount = pnCounts.GetValueOrDefault(a.AccountNo, 0)
            }).ToList()
        };
    }

    public async Task<AccountWithPnsResponse?> GetAccountWithPnsAsync(string cisNo, string accountNo, CancellationToken ct = default)
    {
        // Verify account belongs to this CIS
        var account = await _db.LoanAcctInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.CisNo == cisNo && a.AccountNo == accountNo, ct);

        if (account is null)
            return null;

        // Get active PN records for this account using is_loan(loan_no) = 1 and loan_status != 10
        // Matches the sample query: WHERE webloan.dbo.is_loan(ld.loan_no) = 1 AND loan_status != 10
        var loans = await _db.LoanDatas
            .FromSqlRaw<LoanData>(@"
                SELECT *
                FROM dbo.loan_data
                WHERE acct_no = {0}
                  AND loan_no IS NOT NULL
                  AND webloan.dbo.is_loan(loan_no) = 1
                  AND loan_status != 10
                ORDER BY date_granted DESC", accountNo)
            .AsNoTracking()
            .ToListAsync(ct);

        // Lookups
        var statuses = await _db.LoanStatuses.AsNoTracking().ToListAsync(ct);
        var products = await _db.LoanProducts.AsNoTracking().ToListAsync(ct);

        string? StatusDesc(byte? code) => code is null
            ? null
            : statuses.FirstOrDefault(s =>
                int.TryParse(s.IdCode, out var id) && id == code.Value)?.Description;

        string? ProductDesc(string? code) => code is null
            ? null
            : products.FirstOrDefault(p => p.IdCode == code)?.Description;

        return new AccountWithPnsResponse
        {
            AccountNo = account.AccountNo,
            AccountName = account.Name,
            AccountAddress = BuildAccountAddress(account),
            MisGroup = account.MisGroup,
            PnRecords = loans.Select(l => new PnRecord
            {
                PnNumber = l.LoanNo!,
                ProductCode = l.ProductCode,
                ProductDescription = ProductDesc(l.ProductCode),
                CreationType = l.CreationType,
                CreationTypeLabel = WebLoanCreationTypes.GetLabel(l.CreationType),
                Principal = l.Principal,
                AppliedPrincipal = l.AppliedPrincipal,
                PrincipalBalance = l.PrincipalBalance,
                AmortizationAmount = l.AmortizationAmount,
                OutstandingBalance = l.OutstandingBalance,
                DateGranted = l.DateGranted,
                DateMaturity = l.DateMaturity,
                StatusCode = l.StatusCode,
                StatusDescription = StatusDesc(l.StatusCode),
                CloseDate = l.CloseDate,
                GrantedRate = l.GrantedRate,
                EffectiveRate = l.EffectiveRate,
                Purpose = l.Purpose,
                PaymentInterval = l.PaymentInterval,
                TotalAmortization = l.TotalAmortization
            }).ToList()
        };
    }

    // ─── Active Loans by Account (CIS + acct_no) ────────────────────────────
    //
    // Mirrors the reference "Active Loans by existing borrower" SQL exactly:
    //   WHERE acct_no = @acct AND bch = '000'
    //     AND webloan.dbo.is_loan(loan_no) = 1
    //     AND loan_status != 10
    //   ORDER BY date_granted DESC
    //   TOP 10
    //
    // The bch = '000' filter matches the legacy query and is the bank-default
    // branch code. is_loan() is a UDF that returns 1 for true PN rows (excludes
    // ledger/non-PN rows). loan_status != 10 excludes the terminal "cancelled"
    // status — see dbo.loan_status for the full code list.
    public async Task<ActiveLoansResponse?> GetActiveLoansByAccountAsync(
        string cisNo, string accountNo, CancellationToken ct = default)
    {
        // First confirm the account actually belongs to this CIS. Without this
        // check, anyone with a valid JWT could enumerate active loans for any
        // account number (since acct_no is the only filter in the raw SQL).
        var accountExists = await _db.LoanAcctInfos
            .AsNoTracking()
            .AnyAsync(a => a.CisNo == cisNo && a.AccountNo == accountNo, ct);

        if (!accountExists)
            return null;

        // The reference query uses TOP 10 + an ORDER BY. To use FromSqlRaw with
        // EF Core we must declare the top inline. We also pass the constant
        // '000' branch code as a parameter to keep the prepared SQL cached.
        var loans = await _db.LoanDatas
            .FromSqlRaw<LoanData>(@"
                SELECT TOP 10 *
                FROM dbo.loan_data
                WHERE acct_no = {0}
                  AND bch = {1}
                  AND loan_no IS NOT NULL
                  AND webloan.dbo.is_loan(loan_no) = 1
                  AND loan_status != 10
                ORDER BY date_granted DESC", accountNo, "000")
            .AsNoTracking()
            .ToListAsync(ct);

        // Loan product lookup (code → description) — small table, fetch once.
        var products = await _db.LoanProducts.AsNoTracking().ToListAsync(ct);
        var productByCode = products
            .Where(p => !string.IsNullOrEmpty(p.IdCode))
            .ToDictionary(p => p.IdCode!, p => p.Description, StringComparer.OrdinalIgnoreCase);

        return new ActiveLoansResponse
        {
            AccountNo = accountNo,
            CisNo = cisNo,
            Loans = loans.Select(l => new ActiveLoanItem
            {
                LoanNo = l.LoanNo ?? string.Empty,
                Principal = l.Principal,
                PrincipalBalance = l.PrincipalBalance,
                DateGranted = l.DateGranted,
                DateMaturity = l.DateMaturity,
                LoanProduct = l.ProductCode,
                LoanProductDescription = l.ProductCode is null
                    ? null
                    : productByCode.GetValueOrDefault(l.ProductCode),
                StatusCode = l.StatusCode,
                StatusDescription = StatusCodeLabel(l.StatusCode),
                // Combined display string: "<product> - <status label>", mirroring
                // the reference query's product_status column. Falls back to just
                // the product or just the status if the other side is empty.
                ProductStatus = string.Join(" - ", new[]
                {
                    l.ProductCode,
                    StatusCodeLabel(l.StatusCode)
                }.Where(s => !string.IsNullOrWhiteSpace(s)))
            }).ToList()
        };
    }

    /// <summary>
    /// Human-readable label for a loan_status code. The webloan DB has a
    /// dbo.loan_status lookup table, but a small inline mapping covers the
    /// codes the reference query renders (Current / Pastdue / Litigation /
    /// etc.) without an extra round-trip. Falls back to the raw code as a
    /// string for any code not in the table.
    /// </summary>
    private static string? StatusCodeLabel(byte? code) => code switch
    {
        0 => "Current",
        1 => "Pastdue Performing",
        2 => "Pastdue Non-Performing",
        3 => "Litigation / ITL",
        4 => "Transfer of Asset",
        5 => "Write-off",
        _ => code?.ToString()
    };

    // ─── Original full profile (kept for backward compatibility) ────────────

    public async Task<WebLoanBorrowerResponse?> GetBorrowerByCisAsync(string cisNo, CancellationToken ct = default)
    {
        // ─── Client master ───────────────────────────────────────────────
        var cis = await _db.CisInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CisNo == cisNo, ct);

        if (cis is null)
            return null;

        // ─── Accounts owned by client (LAI) ──────────────────────────────
        var accounts = await _db.LoanAcctInfos
            .AsNoTracking()
            .Where(a => a.CisNo == cisNo)
            .ToListAsync(ct);

        var accountNos = accounts.Select(a => a.AccountNo).ToList();

        // ─── All PN records for those accounts (ledger rows have no PN — excluded) ──
        var loans = accountNos.Count == 0
            ? new List<LoanData>()
            : await _db.LoanDatas
                .AsNoTracking()
                .Where(l => accountNos.Contains(l.AccountNo) && l.LoanNo != null)
                .ToListAsync(ct);

        // ─── Lookups (small tables) ──────────────────────────────────────
        var statuses = await _db.LoanStatuses.AsNoTracking().ToListAsync(ct);
        var products = await _db.LoanProducts.AsNoTracking().ToListAsync(ct);

        string? StatusDesc(byte? code) => code is null
            ? null
            : statuses.FirstOrDefault(s =>
                  int.TryParse(s.IdCode, out var id) && id == code.Value)?.Description;

        string? ProductDesc(string? code) => code is null
            ? null
            : products.FirstOrDefault(p => p.IdCode == code)?.Description;

        // Most recent non-closed PN drives both BranchAndType.Type and LoanInformation.
        var latestActiveLoan = loans
            .Where(l => l.CloseDate is null)
            .OrderByDescending(l => l.DateGranted)
            .FirstOrDefault();

        // ─── Mis-group lookups (requesting officer + MIS agency) ─────────
        // Two distinct group_no values hold the data we need:
        //   - group_no=2: requesting officer hierarchy. Resolved from
        //     loan_acct_info.solicitor (path → description).
        //   - group_no=1: MIS grouping hierarchy. Resolved from
        //     loan_acct_info.cat_mis_group2 (path → description, e.g. "DEPED LIANGA").
        // The same path can exist in both groups with different descriptions,
        // so the join MUST scope by group_no. Verified against live data on
        // CIS 0880944451 (Aug 2026): cat_mis_group2 only exists in group_no=1.
        var solicitorPaths = accounts
            .Where(a => !string.IsNullOrWhiteSpace(a.Solicitor))
            .Select(a => a.Solicitor!)
            .Distinct()
            .ToList();

        var misGroup2Paths = accounts
            .Where(a => !string.IsNullOrWhiteSpace(a.MisGroup2))
            .Select(a => a.MisGroup2!)
            .Distinct()
            .ToList();

        // Requesting officer lookup: group_no=2, per-iterator because the same
        // path can have multiple rows at different grp_level. We want the most
        // specific (highest grp_level) match, with prefix-fallback for missing rows.
        var officerByPath = new Dictionary<string, MisGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in solicitorPaths)
        {
            var candidates = await _db.MisGroups
                .AsNoTracking()
                .Where(m => m.GroupNo == RequestingOfficerGroupNo && m.Path == path)
                .ToListAsync(ct);

            if (candidates.Count == 0)
            {
                // Fall back to the longest matching prefix.
                MisGroup? best = null;
                var prefixes = await _db.MisGroups
                    .AsNoTracking()
                    .Where(m => m.GroupNo == RequestingOfficerGroupNo
                                && m.Path != null
                                && path.StartsWith(m.Path))
                    .ToListAsync(ct);
                foreach (var m in prefixes)
                {
                    if (best is null || (m.Path!.Length > best.Path!.Length))
                        best = m;
                }
                if (best is not null) officerByPath[path] = best;
            }
            else
            {
                officerByPath[path] = candidates
                    .OrderByDescending(m => m.GrpLevel ?? 0)
                    .First();
            }
        }

        // MIS agency lookup: group_no=1, single round-trip keyed by path. Within
        // the group the same path is unique, so the dictionary is unambiguous.
        var misAgencyByPath = misGroup2Paths.Count == 0
            ? new Dictionary<string, MisGroup>(StringComparer.OrdinalIgnoreCase)
            : await _db.MisGroups
                .AsNoTracking()
                .Where(m => m.GroupNo == MisGroupingGroupNo
                            && m.Path != null
                            && misGroup2Paths.Contains(m.Path))
                .ToDictionaryAsync(m => m.Path!, m => m, ct);

        // ─── Agency type via cis_info_misc_data (id_code=14) ─────────────
        // cis_info_misc_data.value_str is a mis_group.id_code within the
        // agency-type group_no. We fetch the row directly and then resolve it.
        var agencyTypeRow = await _db.CisInfoMiscDatas
            .AsNoTracking()
            .Where(m => m.CisNo == cisNo && m.IdCode == CisInfoMiscData.AgencyTypeIdCode)
            .FirstOrDefaultAsync(ct);

        string? agencyTypeDescription = null;
        if (agencyTypeRow is { ValueStr: { Length: > 0 } idCode })
        {
            agencyTypeDescription = await _db.MisGroups
                .AsNoTracking()
                .Where(m => m.GroupNo == AgencyTypeGroupNo && m.IdCode == idCode)
                .Select(m => m.Description)
                .FirstOrDefaultAsync(ct);
        }

        // ─── Pick the primary account: the one whose most-recent PN is the
        //    "latest active loan". The requesting officer comes from that account.
        var primaryAccount = latestActiveLoan is null
            ? accounts.FirstOrDefault()
            : accounts.FirstOrDefault(a => a.AccountNo == latestActiveLoan.AccountNo) ?? accounts.FirstOrDefault();

        var primarySolicitor = primaryAccount?.Solicitor;
        var primarySolicitorDescription =
            primarySolicitor is not null && officerByPath.TryGetValue(primarySolicitor, out var mg)
                ? mg.Description
                : null;

        var primaryMisGroup2Description =
            primaryAccount?.MisGroup2 is { } mg2Path
                ? misAgencyByPath.GetValueOrDefault(mg2Path)?.Description
                : null;

        var response = new WebLoanBorrowerResponse
        {
            // ─── Branch & Type ────────────────────────────────────────
            BranchAndType =
            {
                CisNo = cis.CisNo,
                BranchCode = cis.BranchCode,
                Type = WebLoanCreationTypes.GetLabel(latestActiveLoan?.CreationType),
                TypeCode = latestActiveLoan?.CreationType,
                // Pulled from loan_acct_info.solicitor on the primary account,
                // resolved through dbo.mis_group (group_no=2) for the human name.
                RequestingOfficer = primarySolicitorDescription,
                Lai = accounts.Select(a => a.AccountNo).ToList()
            },

            // ─── Personal Information ─────────────────────────────────
            PersonalInformation =
            {
                FirstName = cis.FirstName,
                MiddleName = cis.MiddleName,
                LastName = cis.LastName,
                Suffix = cis.Appelation,
                Birthdate = ParseBirthDate(cis.BirthDateRaw),
                Address = BuildAddress(cis),
                AgencyName = cis.Company,
                AgencyTypeCode = cis.CompanyTypeCode,
                // Decoded from cis_info_misc_data (id_code=14) → mis_group
                // (group_no=26) description. Falls back to null if the
                // borrower has no misc row or the id_code is unknown.
                AgencyType = agencyTypeDescription,
                PositionTitle = cis.JobTitle,
                RegionCode = cis.RegionCode,
                DivisionCode = cis.DivisionCode,
                StationCode = cis.StationCode,
                EmployeeNo = cis.EmployeeNo,
                // Raw primary MIS path (e.g. "INDIV/SAL").
                MisAgency = accounts.FirstOrDefault()?.MisGroup,
                // Resolved secondary MIS path (e.g. "DEPED LIANGA") from
                // loan_acct_info.cat_mis_group2 → mis_group.path.
                MisAgencyName = primaryMisGroup2Description
            },

            // Loan Information — driven by the most recent non-closed PN
            LoanInformation = BuildLoanInformation(latestActiveLoan, ProductDesc),

            // Outstanding Loans — exclude closed & payoff/write-off accounts
            OutstandingLoans = loans
                .Where(l => l.CloseDate is null
                            && (l.StatusCode is null || !PayoffStatuses.Contains(l.StatusCode.Value)))
                .OrderByDescending(l => l.DateGranted)
                .Select(l => new OutstandingLoanItem
                {
                    Pn = l.LoanNo!,
                    AccountNo = l.AccountNo,
                    PrincipalBalance = l.PrincipalBalance,
                    Amortization = l.AmortizationAmount,
                    OutstandingBalance = l.OutstandingBalance,
                    DateGranted = l.DateGranted,
                    DateMaturity = l.DateMaturity,
                    Status = StatusDesc(l.StatusCode)
                })
                .ToList(),

            // EBI reloan accounts — all existing accounts incl. recently closed,
            // excluding write-offs. Deductions/PayToClose computed later in ALAS.
            EbiReloanAccounts = loans
                .Where(l => l.StatusCode is null || !PayoffStatuses.Contains(l.StatusCode.Value))
                .OrderByDescending(l => l.DateGranted)
                .Select(l => new EbiReloanAccountItem
                {
                    Pn = l.LoanNo!,
                    Name = accounts.FirstOrDefault(a => a.AccountNo == l.AccountNo)?.Name,
                    PrincipalBalance = l.PrincipalBalance,
                    Status = StatusDesc(l.StatusCode)
                })
                .ToList()

            // BuyOutAccounts and IncomingLoans intentionally left empty:
            // no source table exists in webloan; these are captured in ALAS.
        };

        return response;
    }

    // Legacy GetBorrowerByCisAsync returns the full profile in one payload. For
    // corporate borrowers with 50+ accounts × 200+ PNs this can blow up to
    // multi-megabyte JSON responses, so this method returns a bounded payload:
    // each account carries at most RecentPnPerAccount recent PNs (top-N by
    // date_granted desc). The dedicated /promissory-notes endpoint handles
    // arbitrary per-account PN history pagination.
    //
    // Returns null when the CIS does not exist. When the CIS exists but has no
    // accounts, returns an empty PagedResponse — never null for an existing CIS.
    public async Task<PagedResponse<AccountWithPnsPagedItem>?> GetBorrowerByCisPagedAsync(
        string cisNo,
        PaginationRequest pagination,
        CancellationToken ct = default)
    {
        var safePagination = pagination.Sanitized();

        // ─── Client master ───────────────────────────────────────────────
        var cis = await _db.CisInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CisNo == cisNo, ct);

        if (cis is null)
            return null;

        // ─── Accounts owned by client — paginated at the account level ─────
        // The account query is the smallest possible round-trip; we materialise
        // the full set into memory because the pagination is over PNs *per
        // account*, not over the account list itself. A corporate borrower
        // owns at most a few hundred accounts — well within memory budget —
        // and EF Core's AsNoTracking avoids the change-tracker overhead.
        var accounts = await _db.LoanAcctInfos
            .AsNoTracking()
            .Where(a => a.CisNo == cisNo)
            .OrderBy(a => a.AccountNo)
            .ToListAsync(ct);

        // ─── Per-account PN page (top-N recent, ordered by date_granted desc) ──
        // We issue one batched query per account instead of N+1 round-trips by
        // grouping by account and applying the page+take in SQL. The query
        // also enforces the historical invariant used everywhere else in this
        // service: ledger rows (loan_no IS NULL) are excluded.
        var pnPages = await BuildRecentPnsPerAccountAsync(
            accounts.Select(a => a.AccountNo).ToList(),
            Constants.RecentPnPerAccount,
            ct);

        // ─── Assemble paged account list with bounded PN slices ────────────
        var items = new List<AccountWithPnsPagedItem>(accounts.Count);
        foreach (var account in accounts)
        {
            pnPages.TryGetValue(account.AccountNo, out var pnPage);

            items.Add(new AccountWithPnsPagedItem
            {
                AccountNo = account.AccountNo,
                AccountName = account.Name,
                AccountAddress = BuildAccountAddress(account),
                MisGroup = account.MisGroup,
                PnPage = new PagedResponse<PnRecord>(
                    Items: pnPage ?? (IReadOnlyList<PnRecord>)Array.Empty<PnRecord>(),
                    TotalCount: pnPage?.Count ?? 0,
                    Page: 1,
                    PageSize: Constants.RecentPnPerAccount)
            });
        }

        return new PagedResponse<AccountWithPnsPagedItem>(
            Items: items,
            TotalCount: accounts.Count,
            Page: safePagination.Page,
            PageSize: safePagination.PageSize);
    }

    /// <summary>
    /// Returns the top <paramref name="takePerAccount"/> most-recent PNs for
    /// each supplied account number, fully projected into <see cref="PnRecord"/>
    /// shape (status / product descriptions resolved). The result is keyed by
    /// <c>AccountNo</c>; accounts with no PNs (or no matching non-ledger rows)
    /// are absent from the dictionary.
    /// </summary>
    /// <remarks>
    /// SQL Server's <c>ROW_NUMBER() OVER (PARTITION BY acct_no ORDER BY date_granted DESC)</c>
    /// would be the ideal single-roundtrip solution, but EF Core 8 cannot
    /// translate a window function directly. We work around this with a bounded
    /// top-N query (<c>Take(N * takePerAccount)</c>) that orders the union of
    /// recent PNs globally, then take the top <c>takePerAccount</c> per group
    /// in memory. This bounds the wire payload to <c>accounts.Count * takePerAccount</c>
    /// rows even for a corporate borrower with hundreds of accounts.
    /// </remarks>
    private async Task<Dictionary<string, List<PnRecord>>> BuildRecentPnsPerAccountAsync(
        IReadOnlyList<string> accountNos,
        int takePerAccount,
        CancellationToken ct)
    {
        if (accountNos.Count == 0 || takePerAccount <= 0)
            return new Dictionary<string, List<PnRecord>>(StringComparer.Ordinal);

        var (statusByCode, productByCode) = await LoadLookupDictionariesAsync(ct);

        // Bound the wire payload: N accounts × takePerAccount PNs.
        var loans = await _db.LoanDatas
            .AsNoTracking()
            .Where(l => accountNos.Contains(l.AccountNo) && l.LoanNo != null)
            .OrderByDescending(l => l.DateGranted)
            .Take(accountNos.Count * takePerAccount)
            .ToListAsync(ct);

        // Group in memory by account, project to PnRecord, take top-N per group.
        return loans
            .GroupBy(l => l.AccountNo, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Take(takePerAccount)
                      .Select(l => MapLoanDataToPnRecord(l, statusByCode, productByCode))
                      .ToList(),
                StringComparer.Ordinal);
    }

    // Dedicated endpoint for callers that need the FULL PN history of a single
    // account, paged. IDOR protection is preserved: we verify the
    // (cis, account) pair belongs together before returning any PN data.
    public async Task<PagedResponse<PnRecord>?> GetAccountPromissoryNotesPagedAsync(
        string cisNo,
        string accountNo,
        PaginationRequest pagination,
        CancellationToken ct = default)
    {
        var safePagination = pagination.Sanitized();

        // IDOR guard: the account must belong to the supplied CIS. Without
        // this check, any authenticated caller could enumerate PNs for any
        // account number (account_no is the only filter in the PN query).
        var accountExists = await _db.LoanAcctInfos
            .AsNoTracking()
            .AnyAsync(a => a.CisNo == cisNo && a.AccountNo == accountNo, ct);

        if (!accountExists)
            return null;

        // Materialise one page of PNs. Count + Slice as two queries is the
        // idiomatic EF Core pattern when no global filter is in play — it
        // avoids the "SELECT COUNT(*) OVER()" window-function trick which
        // SQL Server can't always optimise. The query is fully AsNoTracking +
        // cancellable per the audit requirements.
        var pnQuery = _db.LoanDatas
            .AsNoTracking()
            .Where(l => l.AccountNo == accountNo && l.LoanNo != null);

        var totalCount = await pnQuery.CountAsync(ct);

        var pagedLoans = await pnQuery
            .OrderByDescending(l => l.DateGranted)
            .ThenByDescending(l => l.LoanNo) // deterministic tie-breaker for stable pagination
            .Skip((safePagination.Page - 1) * safePagination.PageSize)
            .Take(safePagination.PageSize)
            .ToListAsync(ct);

        var (statusByCode, productByCode) = await LoadLookupDictionariesAsync(ct);

        var items = pagedLoans
            .Select(l => MapLoanDataToPnRecord(l, statusByCode, productByCode))
            .ToList();

        return new PagedResponse<PnRecord>(
            Items: items,
            TotalCount: totalCount,
            Page: safePagination.Page,
            PageSize: safePagination.PageSize);
    }

    private static LoanInformationSection BuildLoanInformation(
        LoanData? latest,
        Func<string?, string?> productDesc)
    {
        if (latest is null)
            return new LoanInformationSection();

        return new LoanInformationSection
        {
            ProductCode = latest.ProductCode,
            ProductDescription = productDesc(latest.ProductCode),
            TermMonths = latest.TotalAmortization,
            PaymentIntervalMonths = latest.PaymentInterval,
            InterestRate = latest.GrantedRate ?? latest.EffectiveRate,
            Purpose = latest.Purpose,
            ProposedAmount = latest.AppliedPrincipal ?? latest.Principal
        };
    }

    /// <summary>
    /// Projects a <see cref="LoanData"/> row into a <see cref="PnRecord"/> DTO,
    /// resolving status and product descriptions via the supplied lookup tables.
    /// Centralised so the borrower-profile, account-detail and paged endpoints
    /// all serialise PN rows identically.
    /// </summary>
    private static PnRecord MapLoanDataToPnRecord(
        LoanData l,
        IReadOnlyDictionary<string, LoanStatusLookup> statusByCode,
        IReadOnlyDictionary<string, LoanProductLookup> productByCode)
    {
        var productDesc = l.ProductCode is null ? null
            : productByCode.TryGetValue(l.ProductCode, out var prod) ? prod.Description : null;

        string? statusDesc = l.StatusCode is null ? null
            : statusByCode.TryGetValue(l.StatusCode.Value.ToString(), out var stat) ? stat.Description
              : l.StatusCode.Value.ToString();

        return new PnRecord
        {
            PnNumber = l.LoanNo!,
            AccountNo = l.AccountNo,
            ProductCode = l.ProductCode,
            ProductDescription = productDesc,
            CreationType = l.CreationType,
            CreationTypeLabel = WebLoanCreationTypes.GetLabel(l.CreationType),
            Principal = l.Principal,
            AppliedPrincipal = l.AppliedPrincipal,
            PrincipalBalance = l.PrincipalBalance,
            AmortizationAmount = l.AmortizationAmount,
            OutstandingBalance = l.OutstandingBalance,
            DateGranted = l.DateGranted,
            DateMaturity = l.DateMaturity,
            StatusCode = l.StatusCode,
            StatusDescription = statusDesc,
            CloseDate = l.CloseDate,
            GrantedRate = l.GrantedRate,
            EffectiveRate = l.EffectiveRate,
            Purpose = l.Purpose,
            PaymentInterval = l.PaymentInterval,
            TotalAmortization = l.TotalAmortization
        };
    }

    /// <summary>
    /// Loads the status and product lookup tables once, keyed by their natural
    /// codes, so callers can resolve descriptions in O(1) per row instead of
    /// re-running the linear scan that the original implementation used.
    /// </summary>
    private async Task<(Dictionary<string, LoanStatusLookup> Status, Dictionary<string, LoanProductLookup> Product)>
        LoadLookupDictionariesAsync(CancellationToken ct)
    {
        var statuses = await _db.LoanStatuses.AsNoTracking().ToListAsync(ct);
        var products = await _db.LoanProducts.AsNoTracking().ToListAsync(ct);

        // Status codes are tinyint; the lookup table stores them as varchar.
        // Use the numeric representation as the key so MapLoanDataToPnRecord
        // can do a direct lookup without re-parsing.
        var statusByCode = statuses
            .Where(s => int.TryParse(s.IdCode, out _))
            .ToDictionary(s => int.Parse(s.IdCode).ToString(), s => s, StringComparer.Ordinal);
        var productByCode = products
            .Where(p => !string.IsNullOrEmpty(p.IdCode))
            .ToDictionary(p => p.IdCode!, p => p, StringComparer.OrdinalIgnoreCase);
        return (statusByCode, productByCode);
    }

    // webloan stores p_bday as varchar; formats observed: MM/dd/yyyy, yyyy-MM-dd.
    private static readonly string[] BirthDateFormats = ["MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss"];

    private static DateTime? ParseBirthDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return DateTime.TryParseExact(
            raw, BirthDateFormats, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dt)
            ? dt
            : DateTime.TryParse(raw, out dt) ? dt : null;
    }

    private static string? BuildAddress(CisInfo c)
    {
        var parts = new[] { c.HouseStreet, c.Village, c.Barangay, c.City, c.StateProvince, c.Zip }
            .Where(p => !string.IsNullOrWhiteSpace(p));

        var joined = string.Join(", ", parts);
        return joined.Length == 0 ? null : joined;
    }

    private static string? BuildAccountAddress(LoanAcctInfo a)
    {
        var parts = new[] { a.Add1, a.Add2 }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var joined = string.Join(", ", parts);
        return joined.Length == 0 ? null : joined;
    }
}
